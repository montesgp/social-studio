using SocialStudio.Application.Features.Social.ContentGeneration.Pipeline;

namespace SocialStudio.Tests.Infrastructure.Rendering;

/// <summary>
/// B3 WU-2 test helper.
/// Builds a synthetic <see cref="CompositionSpecEnvelope"/> that mirrors the canonical
/// stub markup produced by <c>StubVisualDesigner.BuildStubMarkup()</c> but with full
/// positional geometry included — enabling 100% offline PPTX adapter tests.
/// </summary>
public static class StubMarkupBuilder
{
    // A tiny 1×1 transparent PNG in base64 — used as the synthetic background image.
    // The PPTX adapter accepts backgroundImageRef as a data-URI: data:image/png;base64,<b64>
    private const string MinimalPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    /// <summary>
    /// Story envelope: 1080×1920, 96 dpi, two data-slot elements (headline + cta),
    /// a data-layer="background" element, and a synthetic inline-PNG backgroundImageRef.
    /// </summary>
    public static CompositionSpecEnvelope BuildStoryEnvelope() =>
        BuildEnvelope(width: 1080, height: 1920, dpi: 96);

    /// <summary>
    /// Carousel slide envelope: 1080×1080, 96 dpi — for slide-dimension assertions.
    /// </summary>
    public static CompositionSpecEnvelope BuildCarouselEnvelope() =>
        BuildEnvelope(width: 1080, height: 1080, dpi: 96);

    /// <summary>
    /// Envelope with null <c>BackgroundImageRef</c> — for fallback / no-background tests.
    /// </summary>
    public static CompositionSpecEnvelope BuildStoryEnvelopeNoBackground() =>
        BuildEnvelope(width: 1080, height: 1920, dpi: 96, includeBackground: false);

    private static CompositionSpecEnvelope BuildEnvelope(
        int width,
        int height,
        int dpi,
        bool includeBackground = true)
    {
        var markup = BuildMarkup(width, height);
        var geometry = BuildGeometry();
        var canvas = new CanvasDimensions(width, height, dpi);
        var backgroundImageRef = includeBackground
            ? $"data:image/png;base64,{MinimalPngBase64}"
            : null;

        return new CompositionSpecEnvelope(
            SchemaVersion: CompositionSpecEnvelope.CurrentSchemaVersion,
            Markup: markup,
            Canvas: canvas,
            SlotGeometry: geometry,
            BackgroundImageRef: backgroundImageRef);
    }

    private static string BuildMarkup(int width, int height) => $"""
        <div class="composition" data-format="story" style="width:{width}px;height:{height}px;">
          <div class="layer-background" data-layer="background" style="background:#000000;"></div>
          <div class="layer-content" data-layer="content">
            <h1 data-slot="headline" style="font-family:Inter;font-size:48px;color:#FFFFFF;text-align:left;">
              Hello World
            </h1>
            <p data-slot="cta" style="font-family:Arial;font-size:20px;color:#FFFF00;text-align:center;">
              Learn More
            </p>
          </div>
        </div>
        """;

    private static IReadOnlyList<SlotGeometryEntry> BuildGeometry() =>
    [
        new SlotGeometryEntry(
            Slot: "headline",
            X: 30,
            Y: 200,
            Width: 900,
            Height: 80,
            FontFamily: "Inter",
            FontSizePx: 48,
            Color: "rgb(255, 255, 255)",
            TextAlign: "left"),
        new SlotGeometryEntry(
            Slot: "cta",
            X: 100,
            Y: 400,
            Width: 600,
            Height: 40,
            FontFamily: "Arial",
            FontSizePx: 20,
            Color: "rgb(255, 255, 0)",
            TextAlign: "center"),
    ];
}
