using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Infrastructure.Rendering.Fonts;
using SocialStudio.Infrastructure.Social.ContentGeneration;
using SkiaSharp;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// client-style-fidelity CSF-PA-02: condensation measured from pixels picks the client's face where a
/// vision model, swayed by stroke weight, kept picking a wider one.
/// </summary>
public sealed class FontCondensationMeterTests
{
    private static readonly string[] Lines = ["Café de", "especialidad", "para sumar a", "tu negocio."];

    [Fact]
    public void Reference_IsMeasuredLineByLine_DespiteTightLeading()
    {
        using var slide = Render("League Gothic", Lines, leading: 0.92);

        var ratios = FontCondensationMeter.MeasureReference(slide, darkGround: true);

        Assert.NotNull(ratios);
        Assert.Equal(Lines.Length, ratios!.Count);
    }

    [Fact]
    public void TheFaceTheSampleWasSetIn_ScoresBest()
    {
        using var slide = Render("League Gothic", Lines, leading: 0.92);
        var reference = FontCondensationMeter.MeasureReference(slide, darkGround: true)!;

        var scores = new[] { "Anton", "League Gothic", "Oswald", "Antonio" }
            .Select(f => (Family: f, Score: FontCondensationMeter.Score(reference, Lines, Face(f))))
            .Where(x => x.Score is not null)
            .OrderBy(x => x.Score)
            .ToList();

        Assert.Equal("League Gothic", scores[0].Family);
        Assert.Equal("Oswald", scores[^1].Family);
    }

    [Fact]
    public void BrightPhotoBesideTheText_DoesNotStretchTheLines()
    {
        // A chrome espresso machine to the right of the headline lit up as "ink" and made every line
        // look as wide as the crop, so a wider face scored best.
        using var slide = Render("League Gothic", Lines, leading: 0.92);
        using (var canvas = new SKCanvas(slide))
        using (var chrome = new SKPaint { Color = SKColors.White })
        {
            canvas.DrawRect(860, 260, 200, 320, chrome);
        }

        var reference = FontCondensationMeter.MeasureReference(slide, darkGround: true)!;
        var best = new[] { "Anton", "League Gothic", "Oswald" }
            .MinBy(f => FontCondensationMeter.Score(reference, Lines, Face(f)) ?? double.MaxValue);

        Assert.Equal(Lines.Length, reference.Count);
        Assert.Equal("League Gothic", best);
    }

    [Fact]
    public void CapsOnlyFace_IsNotScoredForMixedCaseText()
    {
        using var slide = Render("League Gothic", Lines, leading: 0.92);
        var reference = FontCondensationMeter.MeasureReference(slide, darkGround: true)!;

        Assert.Null(FontCondensationMeter.Score(reference, Lines, Face("Bebas Neue")));
    }

    [Fact]
    public async Task Matcher_DecidesByCondensation_WithoutAskingTheModel()
    {
        // The model would answer 1 (Anton, alphabetically first): the measurement must win.
        var gateway = new CountingGateway("""{"heading": 1}""");
        var matcher = new FontCandidateVisualMatcher(new SheetRenderer(), gateway);
        using var slide = Render("League Gothic", Lines, leading: 0.92);
        var request = new FontMatchRequest(
            TypographyRoles.Heading,
            ["Anton", "League Gothic", "Oswald"],
            string.Join("\n", Lines),
            Encode(slide),
            new BrandMarkBoundingBox(0, 0, 1, 1) with { Width = 0.999 });

        var chosen = await matcher.MatchAsync(Guid.NewGuid(), "openai", "gpt", [request], CancellationToken.None);

        Assert.Equal("League Gothic", chosen[TypographyRoles.Heading]);
        Assert.Equal(0, gateway.Calls);
    }

    /// <summary>Light text, one line per entry, on a near-black slide — like the client's.</summary>
    private static SKBitmap Render(string family, IReadOnlyList<string> lines, double leading)
    {
        const int size = 120;
        var bitmap = new SKBitmap(1080, 900);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0x0D, 0x0D, 0x0D));
        using var data = SKData.CreateCopy(Face(family));
        using var typeface = SKTypeface.FromData(data);
        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint { Color = new SKColor(0xF2, 0xED, 0xE2), IsAntialias = true };
        for (var i = 0; i < lines.Count; i++)
        {
            canvas.DrawText(lines[i], 60, 160 + (float)(i * size * leading), font, paint);
        }

        return bitmap;
    }

    private static byte[] Face(string family) =>
        new EmbeddedFontProvider().ResolveFaces(family)
            .OrderBy(f => Math.Abs(Math.Clamp(400, f.MinWeight, f.MaxWeight) - 400))
            .First().Bytes;

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

file sealed class SheetRenderer : IContentRenderer
{
    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct) =>
        Task.FromResult(Result.Success(RenderedAsset.Create(spec.FormatKey, "image/png", "sheet.png", [1, 2, 3])));
}

file sealed class CountingGateway(string answerJson) : IAiCompletionGateway
{
    public int Calls { get; private set; }

    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request,
        CancellationToken cancellationToken = default)
        where TResponse : notnull
    {
        Calls++;
        var payload = JsonSerializer.Deserialize<TResponse>(answerJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(payload, "openai", "gpt", [], null)));
    }
}
