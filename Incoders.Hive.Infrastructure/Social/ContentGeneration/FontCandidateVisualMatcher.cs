using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>One typography role to identify: the client's real sample and the catalog candidates.</summary>
/// <param name="Role">Typography role key (heading/body/caption).</param>
/// <param name="Candidates">Bundled catalog families to compare, most likely first (at least two).</param>
/// <param name="SampleText">The exact words of the client's sample, rendered in every candidate face.</param>
/// <param name="ImageBytes">The reference image holding the sample.</param>
/// <param name="Box">Normalised region of the sample inside <paramref name="ImageBytes"/>.</param>
public sealed record FontMatchRequest(
    string Role,
    IReadOnlyList<string> Candidates,
    string SampleText,
    byte[] ImageBytes,
    BrandMarkBoundingBox Box);

/// <summary>
/// Picks, per typography role, the catalog family whose letterforms match the client's real text
/// (client-style-fidelity CSF-PA-02).
/// </summary>
public interface IFontVisualMatcher
{
    /// <returns>Role → chosen catalog family. Roles that could not be decided are absent.</returns>
    Task<IReadOnlyDictionary<string, string>> MatchAsync(
        Guid organizationId,
        string providerKey,
        string? modelId,
        IReadOnlyList<FontMatchRequest> requests,
        CancellationToken ct);
}

/// <summary>
/// Naming a typeface from memory is where a vision model is weakest: it described Demo Roasters'
/// headline correctly ("heavy condensed grotesque") and still named the wrong face. Comparing is
/// what it does well. This matcher renders the client's own words in each candidate face next to a
/// crop of the real slide and asks which one matches — the same comparison a designer makes.
/// Any failure leaves the extractor's own choice untouched; it never fails an extraction.
/// </summary>
public sealed class FontCandidateVisualMatcher : IFontVisualMatcher
{
    private const string CapabilityKey = "ai-aesthetic";
    private const string PromptVersion = "font-visual-match/v1";
    private const int SheetWidth = 1080;
    private const int MaxSampleLength = 40;

    private readonly IContentRenderer _renderer;
    private readonly IAiCompletionGateway _gateway;
    private readonly ILogger<FontCandidateVisualMatcher> _logger;

    public FontCandidateVisualMatcher(
        IContentRenderer renderer,
        IAiCompletionGateway gateway,
        ILogger<FontCandidateVisualMatcher>? logger = null)
    {
        _renderer = renderer;
        _gateway = gateway;
        _logger = logger ?? NullLogger<FontCandidateVisualMatcher>.Instance;
    }

    public async Task<IReadOnlyDictionary<string, string>> MatchAsync(
        Guid organizationId,
        string providerKey,
        string? modelId,
        IReadOnlyList<FontMatchRequest> requests,
        CancellationToken ct)
    {
        var sections = requests
            .Select(r => (Request: r, Crop: CropToDataUri(r.ImageBytes, r.Box)))
            .Where(s => s.Crop is not null && s.Request.Candidates.Count >= 2)
            .ToArray();
        if (sections.Length == 0)
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var (markup, height) = BuildSheet(sections.Select(s => (s.Request, s.Crop!)).ToArray());
            var spec = new VisualCompositionSpec(
                FormatKey: "font-match",
                LayoutType: "sheet",
                Palette: new CompositionPalette("#000000", "#000000", "#000000", "#111111", "#FFFFFF"),
                Typography: new CompositionTypography("Inter", 24, "Inter", 16),
                AssetSlots: new Dictionary<string, AssetSlotSpec>(),
                CopySlots: new Dictionary<string, string>(),
                Dimensions: new CompositionDimensions(SheetWidth, height, 96))
            {
                OutputKind = CompositionOutputKind.Html,
                DesignerMarkup = markup,
            };

            var rendered = await _renderer.RenderAsync(spec, new RenderOptions(), ct);
            if (rendered.IsFailure)
            {
                _logger.LogWarning("FontVisualMatch: candidate sheet render failed ({Code}); keeping extracted fonts.", rendered.Error.Code);
                return new Dictionary<string, string>();
            }

            var request = new AiJsonCompletionRequest(
                OrganizationId: organizationId,
                AnalysisId: null,
                CapabilityKey: CapabilityKey,
                PromptVersionKey: PromptVersion,
                InputJson: BuildInstruction(sections.Select(s => s.Request.Role)),
                PreferredProviderKey: providerKey,
                PreferredModel: modelId,
                Images: [new AiImageInput(rendered.Value.Mime, Convert.ToBase64String(rendered.Value.GetBytes()))]);

            var answer = await _gateway.CompleteJsonAsync<FontMatchAnswer>(request, ct);
            if (answer.IsFailure)
            {
                _logger.LogWarning("FontVisualMatch: comparison call failed ({Code}); keeping extracted fonts.", answer.Error.Code);
                return new Dictionary<string, string>();
            }

            var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sectionRequest, _) in sections)
            {
                var number = answer.Value.Payload.For(sectionRequest.Role);
                if (number is { } n && n >= 1 && n <= sectionRequest.Candidates.Count)
                {
                    chosen[sectionRequest.Role] = sectionRequest.Candidates[n - 1];
                }
            }

            return chosen;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "FontVisualMatch: comparison failed; keeping extracted fonts.");
            return new Dictionary<string, string>();
        }
    }

    internal static string BuildInstruction(IEnumerable<string> roles) =>
        "The attached image is a typeface comparison sheet. Each section (" +
        string.Join(", ", roles.Select(r => r.ToUpperInvariant())) +
        ") starts with a REFERENCE crop of a client's real slide text, followed by numbered candidate " +
        "rows that render the same words in different typefaces. For each section choose the number of " +
        "the candidate whose LETTERFORMS match the reference best: compare horizontal condensation and " +
        "proportions first, then x-height, stroke weight and contrast, and the shapes of letters such as " +
        "a, e, g, s, S, Q and question marks. Ignore colour, size and background. Return ONLY JSON like " +
        "{\"heading\": 2, \"body\": 1, \"caption\": 3}, with one integer per section shown.";

    /// <summary>
    /// One section per role: the reference crop, then every candidate drawing the sample in its own
    /// face. Candidate families are bundled, so the renderer embeds each one (D2).
    /// </summary>
    internal static (string Markup, int Height) BuildSheet(IReadOnlyList<(FontMatchRequest Request, string CropDataUri)> sections)
    {
        const int sectionHeader = 56;
        const int referenceHeight = 240;
        const int rowHeight = 112;

        var html = new StringBuilder();
        var top = 0;
        foreach (var (request, crop) in sections)
        {
            html.Append(CultureFormat($"<div style=\"position:absolute;left:24px;top:{top + 12}px;font:600 28px 'Inter',sans-serif;color:#111;\">{WebUtility.HtmlEncode(request.Role.ToUpperInvariant())}</div>"));
            top += sectionHeader;
            html.Append(CultureFormat($"<img src=\"{crop}\" style=\"position:absolute;left:24px;top:{top}px;max-width:{SheetWidth - 48}px;height:{referenceHeight - 16}px;object-fit:contain;object-position:left center;\" />"));
            top += referenceHeight;

            var sample = WebUtility.HtmlEncode(Sample(request.SampleText));
            for (var i = 0; i < request.Candidates.Count; i++)
            {
                var family = request.Candidates[i].Replace("'", string.Empty, StringComparison.Ordinal);
                html.Append(CultureFormat($"<div style=\"position:absolute;left:24px;top:{top}px;width:64px;font:700 40px 'Inter',sans-serif;color:#C00;\">{i + 1}</div>"));
                html.Append(CultureFormat($"<div style=\"position:absolute;left:96px;top:{top}px;width:{SheetWidth - 120}px;white-space:nowrap;overflow:hidden;font-family:'{family}',sans-serif;font-size:76px;line-height:1.2;color:#111;\">{sample}</div>"));
                top += rowHeight;
            }

            html.Append(CultureFormat($"<div style=\"position:absolute;left:0;top:{top + 8}px;width:{SheetWidth}px;height:2px;background:#888;\"></div>"));
            top += 24;
        }

        var markup = CultureFormat($"<div style=\"position:relative;width:{SheetWidth}px;height:{top}px;background:#FFFFFF;overflow:hidden;\">{html}</div>");
        return (markup, top);
    }

    private static string CultureFormat(FormattableString value) => FormattableString.Invariant(value);

    private static string Sample(string text)
    {
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxSampleLength ? collapsed : collapsed[..MaxSampleLength];
    }

    /// <summary>Crops the sample (with a small margin) and returns it as a PNG data URI, or null.</summary>
    internal static string? CropToDataUri(byte[] imageBytes, BrandMarkBoundingBox box)
    {
        try
        {
            using var source = SKBitmap.Decode(imageBytes);
            if (source is null)
            {
                return null;
            }

            // A sample a few pixels wide holds no letterforms to compare.
            if (box.Width * source.Width < 12 || box.Height * source.Height < 12)
            {
                return null;
            }

            const double margin = 0.02;
            var left = (int)Math.Floor(Math.Max(0, box.X - margin) * source.Width);
            var top = (int)Math.Floor(Math.Max(0, box.Y - margin) * source.Height);
            var right = (int)Math.Ceiling(Math.Min(1, box.X + box.Width + margin) * source.Width);
            var bottom = (int)Math.Ceiling(Math.Min(1, box.Y + box.Height + margin) * source.Height);
            if (right - left < 8 || bottom - top < 8)
            {
                return null;
            }

            using var cropped = new SKBitmap(right - left, bottom - top);
            if (!source.ExtractSubset(cropped, new SKRectI(left, top, right, bottom)))
            {
                return null;
            }

            using var image = SKImage.FromBitmap(cropped);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return "data:image/png;base64," + Convert.ToBase64String(data.ToArray());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record FontMatchAnswer(
        [property: JsonPropertyName("heading")] int? Heading = null,
        [property: JsonPropertyName("body")] int? Body = null,
        [property: JsonPropertyName("caption")] int? Caption = null)
    {
        public int? For(string role) => role.ToLowerInvariant() switch
        {
            TypographyRoles.Heading => Heading,
            TypographyRoles.Body => Body,
            TypographyRoles.Caption => Caption,
            _ => null,
        };
    }
}
