using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Infrastructure.Rendering;
using A = DocumentFormat.OpenXml.Drawing;

namespace Incoders.Hive.Tests.Infrastructure.Rendering;

/// <summary>
/// B3 WU-2 TDD tests for <see cref="PptxExportAdapter"/>.
///
/// <para>
/// All tests are 100% offline — no Chromium, no network. The adapter is pure:
/// <c>CompositionSpecEnvelope</c> (markup + geometry) → PPTX <c>byte[]</c>.
/// </para>
///
/// Design: PPTX-02 (editable text boxes), PPTX-03 (deterministic offline).
/// D3: geometry consumed as data, adapter never imports Playwright.
/// </summary>
public sealed class PptxExportAdapterTests
{
    private static readonly PptxExportAdapter Adapter = new();

    // ------------------------------------------------------------------
    // Slide count
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_SingleAsset_ProducesOneSlidePart()
    {
        var envelope = StubMarkupBuilder.BuildFlyerEnvelope();

        var result = await Adapter.BuildAsync(envelope, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.MessageKey : "n/a")}");
        var slideCount = CountSlides(result.Value);
        Assert.Equal(1, slideCount);
    }

    [Fact]
    public async Task BuildManyAsync_MultipleEnvelopes_ProducesOneSlidePerEnvelope()
    {
        // A carousel persists one editable envelope per slide; the package builds one PPTX with
        // one slide per envelope so every slide stays editable in PowerPoint/Canva.
        var envelopes = new[]
        {
            StubMarkupBuilder.BuildCarouselEnvelope(),
            StubMarkupBuilder.BuildCarouselEnvelope(),
            StubMarkupBuilder.BuildCarouselEnvelope(),
        };

        var result = await Adapter.BuildManyAsync(envelopes, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.MessageKey : "n/a")}");
        Assert.Equal(3, CountSlides(result.Value));
    }

    [Fact]
    public async Task BuildManyAsync_Empty_ReturnsFailure()
    {
        var result = await Adapter.BuildManyAsync([], CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // ------------------------------------------------------------------
    // Structural validity (Canva import contract)
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_ProducesSchemaValidPptx_WithThemeOnMaster()
    {
        // Canva's Design Import rejects schema-invalid PPTX with an opaque "internal_error". Guard the
        // structural contract that PowerPoint silently repairs but Canva does not: zero OpenXML
        // schema-validation errors AND a theme attached to the slide master.
        var env = StubMarkupBuilder.BuildFlyerEnvelope();
        var result = await Adapter.BuildAsync(env, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.MessageKey : "n/a")}");

        using var ms = new MemoryStream(result.Value);
        using var doc = PresentationDocument.Open(ms, false);

        var master = doc.PresentationPart!.SlideMasterParts.FirstOrDefault();
        Assert.NotNull(master);
        Assert.NotNull(master!.ThemePart);

        var validator = new DocumentFormat.OpenXml.Validation.OpenXmlValidator(
            DocumentFormat.OpenXml.FileFormatVersions.Office2019);
        var errors = validator.Validate(doc).ToList();

        Assert.True(errors.Count == 0,
            "PPTX must be schema-valid for Canva import. Errors:\n" +
            string.Join("\n", errors.Select(e => $"- {e.Part?.Uri} {e.Node?.LocalName}: {e.Description}")));
    }

    [Fact]
    public async Task BuildAsync_MapsTextFidelity_BoldItalicCapsSpacingLineSpacing()
    {
        // Higher-fidelity Canva export: the editable text box must carry the captured CSS styling so
        // it stops looking thin/crude vs the Chromium render. Bold is the single biggest visual gap.
        var markup =
            """<div class="composition" style="width:1080px;height:1920px;"><div data-layer="background" style="background:#000000;"></div><div data-layer="content"><h1 data-slot="headline" style="font-family:Oswald;color:#FFFFFF;">Hola</h1></div></div>""";
        var geometry = new[]
        {
            new SlotGeometryEntry(
                Slot: "headline", X: 40, Y: 200, Width: 1000, Height: 220,
                FontFamily: "Oswald", FontSizePx: 96, Color: "rgb(255,255,255)", TextAlign: "center",
                FontWeight: "800", FontStyle: "italic", LineHeightPx: 110, LetterSpacingPx: 2.5,
                TextTransform: "uppercase"),
        };
        var envelope = new CompositionSpecEnvelope(
            SchemaVersion: CompositionSpecEnvelope.CurrentSchemaVersion,
            Markup: markup,
            Canvas: new CanvasDimensions(1080, 1920, 96),
            SlotGeometry: geometry,
            BackgroundImageRef: null);

        var result = await Adapter.BuildAsync(envelope, CancellationToken.None);
        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.MessageKey : "n/a")}");

        using var ms = new MemoryStream(result.Value);
        using var doc = PresentationDocument.Open(ms, false);
        var slide = doc.PresentationPart!.SlideParts.First().Slide!;

        var runProps = slide.Descendants<A.Run>().First().RunProperties!;
        Assert.True(runProps.Bold?.Value == true, "font-weight 800 must map to bold");
        Assert.True(runProps.Italic?.Value == true, "font-style italic must map to italic");
        Assert.Equal(A.TextCapsValues.All, runProps.Capital?.Value);
        Assert.True(runProps.Spacing is not null && runProps.Spacing.Value > 0, "letter-spacing must map to spc");
        Assert.NotNull(slide.Descendants<A.LineSpacing>().FirstOrDefault());

        var validator = new DocumentFormat.OpenXml.Validation.OpenXmlValidator(
            DocumentFormat.OpenXml.FileFormatVersions.Office2019);
        Assert.Empty(validator.Validate(doc));
    }

    // ------------------------------------------------------------------
    // Slide dimensions — px → EMU via 9525 EMU/px (96 dpi default)
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_SlideSize_MatchesFlyerCanvasInEmu()
    {
        // 1080×1920 at 96 dpi → 9525 EMU/px (914400 EMU/inch ÷ 96 dpi = 9525 exactly)
        // 1080 × 9525 = 10287000 EMU;  1920 × 9525 = 18288000 EMU
        var envelope = StubMarkupBuilder.BuildFlyerEnvelope();

        var result = await Adapter.BuildAsync(envelope, CancellationToken.None);
        Assert.True(result.IsSuccess);

        using var ms = new MemoryStream(result.Value);
        using var prs = PresentationDocument.Open(ms, false);
        var slideSize = prs.PresentationPart!.Presentation!.SlideSize!;

        Assert.Equal(10287000L, (long)slideSize.Cx!);   // 1080 * 9525
        Assert.Equal(18288000L, (long)slideSize.Cy!);   // 1920 * 9525
    }

    [Fact]
    public async Task BuildAsync_SlideSize_MatchesCarouselCanvasInEmu()
    {
        // 1080×1080 → both axes = 1080 × 9525 = 10287000 EMU
        var envelope = StubMarkupBuilder.BuildCarouselEnvelope();

        var result = await Adapter.BuildAsync(envelope, CancellationToken.None);
        Assert.True(result.IsSuccess);

        using var ms = new MemoryStream(result.Value);
        using var prs = PresentationDocument.Open(ms, false);
        var slideSize = prs.PresentationPart!.Presentation!.SlideSize!;

        Assert.Equal(10287000L, (long)slideSize.Cx!);   // 1080 * 9525
        Assert.Equal(10287000L, (long)slideSize.Cy!);   // 1080 * 9525
    }

    // ------------------------------------------------------------------
    // Editable text boxes (PPTX-02-a)
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_DataSlot_ProducesEditableTextBox()
    {
        var envelope = StubMarkupBuilder.BuildFlyerEnvelope();

        var result = await Adapter.BuildAsync(envelope, CancellationToken.None);
        Assert.True(result.IsSuccess);

        var shapes = GetTextShapes(result.Value);
        // We have 2 data-slot elements: headline + cta
        Assert.True(shapes.Count >= 2, $"Expected at least 2 text boxes but found {shapes.Count}.");

        // Each shape must have a TextBody (editable, not rasterized)
        Assert.All(shapes, sp => Assert.NotNull(sp.TextBody));
    }

    [Fact]
    public async Task BuildAsync_DataSlot_TextContentMatchesMarkup()
    {
        var envelope = StubMarkupBuilder.BuildFlyerEnvelope();

        var result = await Adapter.BuildAsync(envelope, CancellationToken.None);
        Assert.True(result.IsSuccess);

        var shapes = GetTextShapes(result.Value);
        var texts = shapes
            .Select(sp => ExtractText(sp))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        // Must contain "Hello World" (headline) and "Learn More" (cta)
        Assert.Contains(texts, t => t!.Contains("Hello World", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(texts, t => t!.Contains("Learn More", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------
    // Geometry → EMU coordinates (D3)
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_TextBox_CoordinatesMatchGeometryInEmu()
    {
        // headline: X=30, Y=200, W=900, H=80 → multiply by 9525
        const double expectedXPx = 30;
        const double expectedYPx = 200;
        const double expectedWPx = 900;
        const double expectedHPx = 80;

        var envelope = StubMarkupBuilder.BuildFlyerEnvelope();

        var result = await Adapter.BuildAsync(envelope, CancellationToken.None);
        Assert.True(result.IsSuccess);

        var shapes = GetTextShapes(result.Value);
        var headlineShape = shapes.FirstOrDefault(s =>
        {
            var text = ExtractText(s);
            return text?.Contains("Hello World", StringComparison.OrdinalIgnoreCase) == true;
        });

        Assert.NotNull(headlineShape);
        var xfrm = headlineShape!
            .ShapeProperties!
            .GetFirstChild<A.Transform2D>()!;

        var off = xfrm.Offset!;
        var ext = xfrm.Extents!;

        Assert.Equal((long)(expectedXPx * 9525), (long)off.X!);
        Assert.Equal((long)(expectedYPx * 9525), (long)off.Y!);
        Assert.Equal((long)(expectedWPx * 9525), (long)ext.Cx!);
        Assert.Equal((long)(expectedHPx * 9525), (long)ext.Cy!);
    }

    // ------------------------------------------------------------------
    // Font and color flow-through
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_TextBox_FontAndColorMatchGeometry()
    {
        var envelope = StubMarkupBuilder.BuildFlyerEnvelope();

        var result = await Adapter.BuildAsync(envelope, CancellationToken.None);
        Assert.True(result.IsSuccess);

        var shapes = GetTextShapes(result.Value);
        // Find the headline shape by text content
        var headlineShape = shapes.FirstOrDefault(s =>
            ExtractText(s)?.Contains("Hello World", StringComparison.OrdinalIgnoreCase) == true);
        Assert.NotNull(headlineShape);

        // Must have at least one run with font size set
        var runs = headlineShape!.TextBody!
            .Descendants<A.Run>()
            .ToList();
        Assert.NotEmpty(runs);

        var runProps = runs.First().RunProperties;
        Assert.NotNull(runProps);
        // Font size in hundredths of a point: 48px * 0.75 pt/px = 36pt → 3600 hundredths
        Assert.Equal(3600, (int)runProps!.FontSize!);
    }

    // ------------------------------------------------------------------
    // Background image layer (PPTX-02-b)
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_Background_ProducesPicturePart()
    {
        var envelope = StubMarkupBuilder.BuildFlyerEnvelope(); // has backgroundImageRef

        var result = await Adapter.BuildAsync(envelope, CancellationToken.None);
        Assert.True(result.IsSuccess);

        // A Picture element must be present in the first slide
        var hasPicture = HasBackgroundPicture(result.Value);
        Assert.True(hasPicture, "Expected a Picture part for the background layer.");
    }

    [Fact]
    public async Task BuildAsync_NoBackground_ProducesNoImagePart()
    {
        var envelope = StubMarkupBuilder.BuildFlyerEnvelopeNoBackground(); // null backgroundImageRef

        var result = await Adapter.BuildAsync(envelope, CancellationToken.None);
        Assert.True(result.IsSuccess);

        var hasPicture = HasBackgroundPicture(result.Value);
        Assert.False(hasPicture, "Expected no Picture part when backgroundImageRef is null.");
    }

    // ------------------------------------------------------------------
    // Null/missing geometry → graceful fallback
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_NullGeometry_FallsBackToStackedLayout()
    {
        // Envelope with no geometry entries
        var envelope = new CompositionSpecEnvelope(
            SchemaVersion: CompositionSpecEnvelope.CurrentSchemaVersion,
            Markup: """
                <div class="composition" style="width:1080px;height:1920px;">
                  <div data-layer="background"></div>
                  <div data-layer="content">
                    <h1 data-slot="headline">Fallback</h1>
                  </div>
                </div>
                """,
            Canvas: new CanvasDimensions(1080, 1920, 96),
            SlotGeometry: [],
            BackgroundImageRef: null);

        var result = await Adapter.BuildAsync(envelope, CancellationToken.None);

        // Must succeed — empty geometry should not throw or fail
        Assert.True(result.IsSuccess, "BuildAsync must succeed even with no geometry.");
        var slides = CountSlides(result.Value);
        Assert.Equal(1, slides);
    }

    // ------------------------------------------------------------------
    // Determinism (PPTX-03-a)
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_Deterministic_SameInputProducesStructurallyIdenticalOutput()
    {
        var envelope = StubMarkupBuilder.BuildFlyerEnvelope();

        var result1 = await Adapter.BuildAsync(envelope, CancellationToken.None);
        var result2 = await Adapter.BuildAsync(envelope, CancellationToken.None);

        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);

        // Compare structural properties (not byte equality — timestamps/UUIDs in XML may differ)
        Assert.Equal(CountSlides(result1.Value), CountSlides(result2.Value));
        Assert.Equal(GetTextShapes(result1.Value).Count, GetTextShapes(result2.Value).Count);
        Assert.Equal(HasBackgroundPicture(result1.Value), HasBackgroundPicture(result2.Value));

        var texts1 = GetTextShapes(result1.Value).Select(ExtractText).OrderBy(t => t).ToList();
        var texts2 = GetTextShapes(result2.Value).Select(ExtractText).OrderBy(t => t).ToList();
        Assert.Equal(texts1, texts2);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static int CountSlides(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var prs = PresentationDocument.Open(ms, false);
        return prs.PresentationPart!.SlideParts.Count();
    }

    private static List<Shape> GetTextShapes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var prs = PresentationDocument.Open(ms, false);
        return prs.PresentationPart!.SlideParts
            .SelectMany(sp => sp.Slide!.Descendants<Shape>())
            .Where(sh => sh.TextBody is not null)
            .ToList();
    }

    private static bool HasBackgroundPicture(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var prs = PresentationDocument.Open(ms, false);
        return prs.PresentationPart!.SlideParts
            .Any(sp => sp.Slide!.Descendants<Picture>().Any());
    }

    private static string? ExtractText(Shape shape)
    {
        var runs = shape.TextBody?.Descendants<A.Run>() ?? [];
        var text = string.Concat(runs.Select(r => r.Text?.Text ?? string.Empty));
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
