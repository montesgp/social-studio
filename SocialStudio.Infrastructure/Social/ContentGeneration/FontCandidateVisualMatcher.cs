using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Fonts;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Infrastructure.Rendering.Fonts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace SocialStudio.Infrastructure.Social.ContentGeneration;

/// <summary>One typography role to identify: the client's real sample and the catalog candidates.</summary>
/// <param name="Role">Typography role key (heading/body/caption).</param>
/// <param name="Candidates">Bundled catalog families to compare, most likely first (at least two).</param>
/// <param name="SampleText">The exact words of the client's sample, rendered in every candidate face.</param>
/// <param name="ImageBytes">The reference image holding the sample.</param>
/// <param name="Box">Normalised region of the sample inside <paramref name="ImageBytes"/>.</param>
/// <param name="SourceLabel">Where <paramref name="ImageBytes"/> came from (storage path), for diagnostics.</param>
public sealed record FontMatchRequest(
    string Role,
    IReadOnlyList<string> Candidates,
    string SampleText,
    byte[] ImageBytes,
    BrandMarkBoundingBox Box,
    string? SourceLabel = null);

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
    private const string PromptVersion = "font-visual-match/v5";
    private const string LocatePromptVersion = "font-sample-locate/v1";
    private const int SheetWidth = 1080;
    private const int MaxLineLength = 24;

    private readonly IContentRenderer _renderer;
    private readonly IAiCompletionGateway _gateway;
    private readonly ILogger<FontCandidateVisualMatcher> _logger;
    private readonly IFontProvider _fonts;

    public FontCandidateVisualMatcher(
        IContentRenderer renderer,
        IAiCompletionGateway gateway,
        ILogger<FontCandidateVisualMatcher>? logger = null,
        IFontProvider? fonts = null)
    {
        _renderer = renderer;
        _gateway = gateway;
        _fonts = fonts ?? new EmbeddedFontProvider();
        _logger = logger ?? NullLogger<FontCandidateVisualMatcher>.Instance;
    }

    public async Task<IReadOnlyDictionary<string, string>> MatchAsync(
        Guid organizationId,
        string providerKey,
        string? modelId,
        IReadOnlyList<FontMatchRequest> requests,
        CancellationToken ct)
    {
        // Roles are compared one at a time, heading first: the AI gateway shares a scoped DbContext and
        // cannot run calls concurrently, and the heading is the role that defines the client's look. A
        // role finished before the time budget runs out is kept even if a later role is cut short.
        var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var request in requests.OrderBy(r => RoleOrder(r.Role)))
        {
            try
            {
                if (await MatchRoleAsync(organizationId, providerKey, modelId, request, ct) is { } family)
                {
                    chosen[request.Role] = family;
                }
            }
            catch (OperationCanceledException) when (chosen.Count > 0)
            {
                _logger.LogWarning(
                    "FontVisualMatch: out of time after {Done}; keeping those and the extracted fonts for the rest.",
                    string.Join(", ", chosen.Keys));
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "FontVisualMatch: {Role} comparison failed; keeping its extracted font.", request.Role);
            }
        }

        return chosen;
    }

    /// <summary>
    /// A condensation score (mean |log ratio| per line) this low means the face's line widths track the
    /// client's within about 30 %, and a lead this large over the runner-up makes the pick unambiguous.
    /// Calibrated on Demo Roasters samples, where the right face scored 0.04-0.23 and led by 0.06+.
    /// </summary>
    private const double DecisiveScore = 0.35;
    private const double DecisiveMargin = 0.05;

    /// <summary>
    /// Candidates ordered by how closely their per-line condensation matches the sample's, best first.
    /// Empty when the sample's lines cannot be measured or do not match the reported line breaks.
    /// </summary>
    internal IReadOnlyList<(string Family, double Score)> RankByCondensation(FontMatchRequest r, SampleCrop crop)
    {
        var lines = FullSampleLines(r.SampleText);
        if (crop.LineRatios is not { } reference || reference.Count != lines.Count)
        {
            return [];
        }

        var ranking = new List<(string Family, double Score)>();
        foreach (var family in r.Candidates)
        {
            var face = _fonts.ResolveFaces(family)
                .OrderBy(f => Math.Abs(Math.Clamp(400, f.MinWeight, f.MaxWeight) - 400))
                .FirstOrDefault();
            if (face is not null && FontCondensationMeter.Score(reference, lines, face.Bytes) is { } score)
            {
                ranking.Add((family, score));
            }
        }

        return ranking.OrderBy(x => x.Score).ToList();
    }

    /// <summary>The sample's lines, untruncated: truncating would distort the measured widths.</summary>
    private static IReadOnlyList<string> FullSampleLines(string text) =>
        text.Replace("\\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(l => string.Join(' ', l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .ToList();

    private static int RoleOrder(string role) => role.ToLowerInvariant() switch
    {
        TypographyRoles.Heading => 0,
        TypographyRoles.Body => 1,
        _ => 2,
    };

    /// <summary>
    /// Compares one role: locates its sample when the extractor did not, renders the candidate sheet
    /// and asks which face matches. Null when the role cannot be compared or the model gives no
    /// usable answer.
    /// </summary>
    private async Task<string?> MatchRoleAsync(
        Guid organizationId,
        string providerKey,
        string? modelId,
        FontMatchRequest request,
        CancellationToken ct)
    {
        // A sample located inside the big extraction call often comes back without a box; a dedicated
        // single-slide question finds it reliably.
        var r = IsWholeImage(request.Box)
            ? request with { Box = await LocateSampleAsync(organizationId, providerKey, modelId, request, ct) ?? request.Box }
            : request;

        // Alphabetical order: the first candidate is otherwise always the model's own first guess, and
        // the comparison answered "1" far more often than the pixels justified.
        r = r with { Candidates = r.Candidates.OrderBy(c => c, StringComparer.Ordinal).ToArray() };
        var crop = CropSample(r.ImageBytes, r.Box);
        if (crop is null || r.Candidates.Count < 2)
        {
            return null;
        }

        // Pixels decide condensation; the vision model only breaks a close call on letter shapes.
        var ranking = RankByCondensation(r, crop);
        if (ranking.Count >= 2)
        {
            _logger.LogInformation(
                "FontVisualMatch: {Role} condensation ranking {Ranking}.",
                r.Role, string.Join(", ", ranking.Select(x => $"{x.Family}={x.Score:0.###}")));

            if (ranking[0].Score <= DecisiveScore && ranking[1].Score - ranking[0].Score >= DecisiveMargin)
            {
                return ranking[0].Family;
            }

            r = r with
            {
                Candidates = ranking.Take(3).Select(x => x.Family).OrderBy(c => c, StringComparer.Ordinal).ToArray(),
            };
        }

        // One sheet per role, each its own image: a single sheet holding every role was so tall that the
        // vision model received it heavily downscaled and the tiles were too small to compare.
        var (markup, height) = BuildSheet([(r, crop)]);
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
            _logger.LogWarning("FontVisualMatch: {Role} sheet render failed ({Code}).", r.Role, rendered.Error.Code);
            return null;
        }

        var bytes = rendered.Value.GetBytes();
        var sheetPath = SaveDiagnosticSheet(bytes);
        _logger.LogInformation(
            "FontVisualMatch: {Role} from {Source} box=({X:0.###},{Y:0.###},{W:0.###},{H:0.###}) dark={Dark} lines=[{Lines}] candidates=[{Candidates}]{Sheet}.",
            r.Role, r.SourceLabel ?? "?", r.Box.X, r.Box.Y, r.Box.Width, r.Box.Height, crop.IsDark,
            string.Join(" | ", SampleLines(r.SampleText)), string.Join(", ", r.Candidates),
            sheetPath is null ? string.Empty : $" sheet={sheetPath}");

        var answer = await CompareRoleAsync(
            organizationId, providerKey, modelId, r, new AiImageInput(rendered.Value.Mime, Convert.ToBase64String(bytes)), ct);
        _logger.LogInformation("FontVisualMatch: {Role} answer {Answer}.", r.Role, answer?.ToString() ?? "none");

        return answer is { } n && n >= 1 && n <= r.Candidates.Count ? r.Candidates[n - 1] : null;
    }

    /// <summary>The candidate number chosen for one role, or null when the call failed.</summary>
    private async Task<int?> CompareRoleAsync(
        Guid organizationId,
        string providerKey,
        string? modelId,
        FontMatchRequest r,
        AiImageInput sheet,
        CancellationToken ct)
    {
        var answer = await _gateway.CompleteJsonAsync<FontMatchAnswer>(
            new AiJsonCompletionRequest(
                OrganizationId: organizationId,
                AnalysisId: null,
                CapabilityKey: CapabilityKey,
                PromptVersionKey: PromptVersion,
                InputJson: BuildInstruction([(r.Role, r.SampleText)]),
                PreferredProviderKey: providerKey,
                PreferredModel: modelId,
                Images: [sheet, new AiImageInput(MimeOf(r.ImageBytes), Convert.ToBase64String(r.ImageBytes))]),
            ct);
        if (answer.IsFailure)
        {
            _logger.LogWarning("FontVisualMatch: {Role} comparison call failed ({Code}).", r.Role, answer.Error.Code);
            return null;
        }

        return answer.Value.Payload.For(r.Role);
    }

    /// <summary>
    /// Directory where comparison sheets are written for inspection, from the
    /// <c>HIVE_FONT_MATCH_DIAGNOSTICS_DIR</c> environment variable. Unset (the default) writes nothing.
    /// </summary>
    private const string DiagnosticsDirVariable = "HIVE_FONT_MATCH_DIAGNOSTICS_DIR";

    private string? SaveDiagnosticSheet(byte[] png)
    {
        var dir = Environment.GetEnvironmentVariable(DiagnosticsDirVariable);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"font-match-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.png");
            File.WriteAllBytes(path, png);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "FontVisualMatch: could not write the diagnostic sheet to {Dir}.", dir);
            return null;
        }
    }

    private static bool IsWholeImage(BrandMarkBoundingBox box) =>
        box.X <= 0 && box.Y <= 0 && box.Width >= 1 && box.Height >= 1;

    /// <summary>
    /// Asks the vision route where <see cref="FontMatchRequest.SampleText"/> sits on its one slide.
    /// Null when it cannot say; the caller then compares against the whole slide.
    /// </summary>
    private async Task<BrandMarkBoundingBox?> LocateSampleAsync(
        Guid organizationId, string providerKey, string? modelId, FontMatchRequest r, CancellationToken ct)
    {
        try
        {
            var answer = await _gateway.CompleteJsonAsync<LocatedBox>(
                new AiJsonCompletionRequest(
                    OrganizationId: organizationId,
                    AnalysisId: null,
                    CapabilityKey: CapabilityKey,
                    PromptVersionKey: LocatePromptVersion,
                    InputJson: BuildLocateInstruction(r.SampleText),
                    PreferredProviderKey: providerKey,
                    PreferredModel: modelId,
                    Images: [new AiImageInput(MimeOf(r.ImageBytes), Convert.ToBase64String(r.ImageBytes))]),
                ct);
            if (answer.IsFailure)
            {
                return null;
            }

            var box = answer.Value.Payload;
            var valid = BrandMarkDetection.TryValidateBoundingBox(box.X, box.Y, box.Width, box.Height);
            _logger.LogInformation(
                "FontVisualMatch: located {Role} sample at ({X},{Y},{W},{H}) -> {Outcome}.",
                r.Role, box.X, box.Y, box.Width, box.Height, valid is null ? "unusable" : "used");
            return valid;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "FontVisualMatch: locating the {Role} sample failed; comparing against the whole slide.", r.Role);
            return null;
        }
    }

    internal static string BuildLocateInstruction(string sampleText) =>
        "Locate the text block \"" + Flatten(sampleText) + "\" in the attached image and return ONLY JSON " +
        "{\"x\":0..1,\"y\":0..1,\"width\":0..1,\"height\":0..1} as fractions of the image width and " +
        "height, origin top-left, tight around the whole block.";

    private sealed record LocatedBox(
        [property: JsonPropertyName("x")] double? X = null,
        [property: JsonPropertyName("y")] double? Y = null,
        [property: JsonPropertyName("width")] double? Width = null,
        [property: JsonPropertyName("height")] double? Height = null);

    internal static string BuildInstruction(IEnumerable<(string Role, string SampleText)> sections)
    {
        var list = sections.ToList();
        var count = list.Count;
        var sheets = string.Join("; ", list.Select((s, i) =>
            $"Image {i + 1} is the comparison sheet for {s.Role.ToUpperInvariant()}"));
        var originals = string.Join("; ", list.Select((s, i) =>
            $"Image {count + i + 1} is the client's original slide for {s.Role.ToUpperInvariant()}, where the " +
            $"reference text \"{Flatten(s.SampleText)}\" appears"));
        return sheets + ". In each sheet the tile labelled REFERENCE is a crop of the client's real text, and " +
            "the numbered tiles render the same words, with the same line breaks and colours, in different " +
            "typefaces. " + originals + " — use it to see the reference at full resolution, since the crop " +
            "may be loose. For each role choose the number whose LETTERFORMS match the client's text best: " +
            "compare horizontal condensation (how narrow each letter is relative to its height) first, then " +
            "letter case, stroke weight and the shapes of letters such as a, e, g, s, S and question marks. " +
            "A tile whose text overflows its edge is a WIDER face than the reference. Ignore size and colour. " +
            "Return ONLY JSON like {\"heading\": 2, \"body\": 1, \"caption\": 3}, with one integer per role shown.";
    }

    private static string Flatten(string text) =>
        string.Join(' ', SampleLines(text)).Replace("\"", "'", StringComparison.Ordinal);

    /// <summary>JPEG unless the bytes carry the PNG signature (reference images are archived JPEGs).</summary>
    private static string MimeOf(byte[] bytes) =>
        bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            ? "image/png"
            : "image/jpeg";

    private const int Tile = 340;
    private const int Gap = 12;
    private const int TilePadding = 20;
    private const int SectionHeader = 48;
    private const int Columns = 3;

    /// <summary>
    /// One section per role, laid out as a grid of equal tiles: the reference crop first, then every
    /// candidate drawing the sample in its own face with the client's line breaks and the reference's
    /// light/dark scheme. Rendering candidates at another scale, on one line and in other colours made
    /// the comparison unstable — the same client came back as League Gothic on one run and Anton on
    /// the next. Candidate families are bundled, so the renderer embeds each one (D2).
    /// </summary>
    internal static (string Markup, int Height) BuildSheet(IReadOnlyList<(FontMatchRequest Request, SampleCrop Crop)> sections)
    {
        var html = new StringBuilder();
        var top = 0;
        foreach (var (request, crop) in sections)
        {
            html.Append(CultureFormat($"<div style=\"position:absolute;left:{Gap}px;top:{top + 8}px;font:600 26px 'Inter',sans-serif;color:#111;\">{WebUtility.HtmlEncode(request.Role.ToUpperInvariant())}</div>"));
            top += SectionHeader;

            var (background, foreground) = crop.IsDark ? ("#111111", "#F2EDE2") : ("#F5F5F5", "#111111");
            var lines = SampleLines(request.SampleText);
            var text = string.Join("<br>", lines.Select(WebUtility.HtmlEncode));
            // Same scale as the reference: the crop is drawn with object-fit:contain in the tile, so
            // its displayed height is known from its aspect; the candidate block gets that height.
            // Drawn bigger, the candidates overflowed and stopped looking like the reference at all.
            var inner = Tile - (2 * TilePadding);
            var referenceHeight = crop.Aspect >= 1 ? inner / crop.Aspect : inner;
            var fontSize = (int)Math.Clamp(referenceHeight / lines.Count, 14, 110);

            var tiles = new List<string>
            {
                TileHtml(background, "REFERENCE",
                    CultureFormat($"<img src=\"{crop.DataUri}\" style=\"position:absolute;left:{TilePadding}px;top:{TilePadding}px;width:{Tile - (2 * TilePadding)}px;height:{Tile - (2 * TilePadding)}px;object-fit:contain;object-position:left top;\" />")),
            };
            for (var i = 0; i < request.Candidates.Count; i++)
            {
                var family = request.Candidates[i].Replace("'", string.Empty, StringComparison.Ordinal);
                tiles.Add(TileHtml(background, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CultureFormat($"<div style=\"position:absolute;left:{TilePadding}px;top:{TilePadding}px;white-space:nowrap;font-family:'{family}',sans-serif;font-size:{fontSize}px;line-height:1;color:{foreground};\">{text}</div>")));
            }

            for (var t = 0; t < tiles.Count; t++)
            {
                var left = Gap + ((t % Columns) * (Tile + Gap));
                var tileTop = top + ((t / Columns) * (Tile + Gap));
                html.Append(CultureFormat($"<div style=\"position:absolute;left:{left}px;top:{tileTop}px;\">{tiles[t]}</div>"));
            }

            var rows = (tiles.Count + Columns - 1) / Columns;
            top += (rows * (Tile + Gap)) + Gap;
        }

        var markup = CultureFormat($"<div style=\"position:relative;width:{SheetWidth}px;height:{top}px;background:#FFFFFF;overflow:hidden;\">{html}</div>");
        return (markup, top);
    }

    private static string TileHtml(string background, string label, string content) =>
        CultureFormat($"<div style=\"position:relative;width:{Tile}px;height:{Tile}px;background:{background};overflow:hidden;\">{content}<div style=\"position:absolute;right:8px;top:4px;font:700 26px 'Inter',sans-serif;color:#E33;\">{WebUtility.HtmlEncode(label)}</div></div>");

    private static string CultureFormat(FormattableString value) => FormattableString.Invariant(value);

    /// <summary>
    /// The sample's lines as the client breaks them (the extractor reports them separated by
    /// newlines), at most five, each trimmed to a length a tile can hold.
    /// </summary>
    internal static IReadOnlyList<string> SampleLines(string text)
    {
        var lines = text
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(l => string.Join(' ', l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Select(l => l.Length <= MaxLineLength ? l : l[..MaxLineLength])
            .Take(5)
            .ToList();
        return lines.Count > 0 ? lines : [text.Length <= MaxLineLength ? text : text[..MaxLineLength]];
    }

    /// <summary>A cropped text sample and whether its ground is dark.</summary>
    internal sealed record SampleCrop(
        string DataUri,
        bool IsDark,
        double Aspect = 1,
        IReadOnlyList<double>? LineRatios = null);

    /// <summary>
    /// Crops the sample (with a small margin) to a PNG data URI and measures whether its ground is dark,
    /// so the candidate tiles use the same light/dark scheme. Null when the sample cannot be cropped.
    /// </summary>
    internal static SampleCrop? CropSample(byte[] imageBytes, BrandMarkBoundingBox box)
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
            var dark = IsDark(cropped);
            return new SampleCrop(
                "data:image/png;base64," + Convert.ToBase64String(data.ToArray()),
                dark,
                (double)cropped.Width / cropped.Height,
                FontCondensationMeter.MeasureReference(cropped, dark));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Mean luminance below the midpoint: most of a text block's pixels are its ground.</summary>
    private static bool IsDark(SKBitmap bitmap)
    {
        double sum = 0;
        var count = 0;
        var step = Math.Max(1, Math.Min(bitmap.Width, bitmap.Height) / 32);
        for (var y = 0; y < bitmap.Height; y += step)
        {
            for (var x = 0; x < bitmap.Width; x += step)
            {
                var c = bitmap.GetPixel(x, y);
                sum += (0.2126 * c.Red) + (0.7152 * c.Green) + (0.0722 * c.Blue);
                count++;
            }
        }

        return count > 0 && sum / count < 128;
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
