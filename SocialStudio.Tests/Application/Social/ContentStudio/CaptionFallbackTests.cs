using SocialStudio.Application.Features.Social.ContentGeneration.Pipeline;
using SocialStudio.Application.Features.Social.ContentStudio.Materialize;

namespace SocialStudio.Tests.Application.Social.ContentStudio;

/// <summary>
/// TDD tests for <see cref="CaptionFallback"/> — task 3.7/3.8 (Content Studio editing completeness,
/// Decision E3/Q2). Derives a deterministic, editable caption from content already present (the
/// first slide's headline) when the AI-generated markup carried no caption — never a hard job
/// failure, never a literal placeholder string.
/// </summary>
public sealed class CaptionFallbackTests
{
    [Fact]
    public void DeriveFromCompositionSpec_ExtractsFirstSlotText()
    {
        var envelope = new CompositionSpecEnvelope(
            SchemaVersion: 1,
            Markup: """<div class="composition"><div data-layer="content"><h1 data-slot="headline">Grow your brand today</h1></div></div>""",
            Canvas: new CanvasDimensions(1080, 1920, 96),
            SlotGeometry: Array.Empty<SlotGeometryEntry>());
        var json = CompositionSpecJson.Serialize([envelope]);

        var result = CaptionFallback.DeriveFromCompositionSpec(json);

        Assert.Equal("Grow your brand today", result);
    }

    [Fact]
    public void DeriveFromCompositionSpec_StripsNestedTags()
    {
        var envelope = new CompositionSpecEnvelope(
            SchemaVersion: 1,
            Markup: """<div class="composition"><div data-layer="content"><h1 data-slot="headline">Grow <strong>your</strong> brand</h1></div></div>""",
            Canvas: new CanvasDimensions(1080, 1920, 96),
            SlotGeometry: Array.Empty<SlotGeometryEntry>());
        var json = CompositionSpecJson.Serialize([envelope]);

        var result = CaptionFallback.DeriveFromCompositionSpec(json);

        Assert.Equal("Grow your brand", result);
    }

    [Fact]
    public void DeriveFromCompositionSpec_NullJson_ReturnsNull()
    {
        Assert.Null(CaptionFallback.DeriveFromCompositionSpec(null));
    }

    [Fact]
    public void DeriveFromCompositionSpec_NoSlotText_ReturnsNull()
    {
        var envelope = new CompositionSpecEnvelope(
            SchemaVersion: 1,
            Markup: """<div class="composition"><div data-layer="background"></div></div>""",
            Canvas: new CanvasDimensions(1080, 1920, 96),
            SlotGeometry: Array.Empty<SlotGeometryEntry>());
        var json = CompositionSpecJson.Serialize([envelope]);

        Assert.Null(CaptionFallback.DeriveFromCompositionSpec(json));
    }

    [Fact]
    public void DeriveFromCompositionSpec_UsesFirstActiveEnvelope_ForMultiSlideCarousel()
    {
        var envelope1 = new CompositionSpecEnvelope(
            SchemaVersion: 1,
            Markup: """<div class="composition"><div data-layer="content"><h1 data-slot="headline">Slide one headline</h1></div></div>""",
            Canvas: new CanvasDimensions(1080, 1350, 96),
            SlotGeometry: Array.Empty<SlotGeometryEntry>());
        var envelope2 = new CompositionSpecEnvelope(
            SchemaVersion: 1,
            Markup: """<div class="composition"><div data-layer="content"><h1 data-slot="headline">Slide two headline</h1></div></div>""",
            Canvas: new CanvasDimensions(1080, 1350, 96),
            SlotGeometry: Array.Empty<SlotGeometryEntry>());
        var json = CompositionSpecJson.Serialize([envelope1, envelope2]);

        var result = CaptionFallback.DeriveFromCompositionSpec(json);

        Assert.Equal("Slide one headline", result);
    }
}
