using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// Additive extension tests for <see cref="AestheticExtraction"/>.
/// Covers client-aligned-carousel-generation fields (BackgroundPattern, ColorPairings) and
/// WU-1 task 1.9 [RED] additions (ConceptualMethod, ImageTypeRatio; CACD-PA-04-a/b/c).
/// Refs: design §3.1; tasks 1.7/1.8 (carousel), 1.9/1.10 (conceptual); CAC-BSP-01-b; CACD-PA-04.
/// </summary>
public sealed class AestheticExtractionExtensionTests
{
    // ── Existing construction compiles unchanged ───────────────────────────────

    [Fact]
    public void ExistingConstruction_FourParamForm_CompilesAndNewFieldsAreNull()
    {
        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: ["editorial"],
            Motifs: [],
            CaptionVoice: "playful");

        // CAC-BSP-01-b shape check: existing construction unchanged
        Assert.Null(extraction.BackgroundPattern);
        Assert.Null(extraction.ColorPairings);
    }

    // ── BackgroundPattern field accessible ────────────────────────────────────

    [Fact]
    public void BackgroundPattern_WhenSet_IsIReadOnlyListOfSlideBackgroundKind()
    {
        IReadOnlyList<SlideBackgroundKind> pattern = [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor];

        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            BackgroundPattern: pattern);

        Assert.NotNull(extraction.BackgroundPattern);
        Assert.Equal(2, extraction.BackgroundPattern!.Count);
        Assert.Equal(SlideBackgroundKind.Photo, extraction.BackgroundPattern[0]);
    }

    // ── ColorPairings field accessible ────────────────────────────────────────

    [Fact]
    public void ColorPairings_WhenSet_IsIReadOnlyListOfColorPairing()
    {
        IReadOnlyList<ColorPairing> pairings = [new ColorPairing("#1A2B3C", "#FFFFFF", 1)];

        var extraction = new AestheticExtraction(
            Palette: [],
            StyleDescriptors: [],
            Motifs: [],
            CaptionVoice: null,
            ColorPairings: pairings);

        Assert.NotNull(extraction.ColorPairings);
        Assert.Single(extraction.ColorPairings!);
        Assert.Equal("#1A2B3C", extraction.ColorPairings![0].BackgroundHex);
    }

    // ── Null-safe: both fields default to null when omitted ───────────────────

    [Fact]
    public void BothFields_DefaultToNull_WhenOmitted()
    {
        var extraction = new AestheticExtraction([], [], [], null);

        Assert.Null(extraction.BackgroundPattern);
        Assert.Null(extraction.ColorPairings);
    }

    // ── AestheticExtraction.Empty still works (no exception) ─────────────────

    [Fact]
    public void Empty_StaticProperty_StillCompiles_NewFieldsAreNull()
    {
        var empty = AestheticExtraction.Empty;

        Assert.NotNull(empty);
        Assert.Null(empty.BackgroundPattern);
        Assert.Null(empty.ColorPairings);
    }

    // ── with-expression on new fields compiles ────────────────────────────────

    [Fact]
    public void WithExpression_BackgroundPattern_CompilesAndYieldsNewValue()
    {
        var original = new AestheticExtraction([], [], [], null);
        var updated = original with { BackgroundPattern = [SlideBackgroundKind.Photo] };

        Assert.Null(original.BackgroundPattern);
        Assert.Single(updated.BackgroundPattern!);
    }

    [Fact]
    public void WithExpression_ColorPairings_CompilesAndYieldsNewValue()
    {
        var original = new AestheticExtraction([], [], [], null);
        var updated = original with { ColorPairings = [new ColorPairing("#ABABAB", "#000000", 2)] };

        Assert.Null(original.ColorPairings);
        Assert.Single(updated.ColorPairings!);
    }

    // ── WU-1 task 1.9 [RED]: ConceptualMethod field (CACD-PA-04-a) ───────────

    [Fact]
    public void ConceptualMethod_DefaultsToNull_WhenOmittedFromConstruction()
    {
        var extraction = new AestheticExtraction([], [], [], null);

        Assert.Null(extraction.ConceptualMethod);
    }

    [Fact]
    public void ConceptualMethod_WhenSet_IsStoredCorrectly()
    {
        var cm = new ConceptualMethod(ArtDirectionMethod.MetaphoricalEditorial, "Metaphor", ["mono-color backdrop"]);
        var extraction = new AestheticExtraction([], [], [], null, ConceptualMethod: cm);

        Assert.NotNull(extraction.ConceptualMethod);
        Assert.Equal(ArtDirectionMethod.MetaphoricalEditorial, extraction.ConceptualMethod!.Method);
    }

    // ── WU-1 task 1.9 [RED]: ImageTypeRatio field (CACD-PA-04-b) ────────────

    [Fact]
    public void ImageTypeRatio_DefaultsToNull_WhenOmittedFromConstruction()
    {
        var extraction = new AestheticExtraction([], [], [], null);

        Assert.Null(extraction.ImageTypeRatio);
    }

    [Fact]
    public void ImageTypeRatio_WhenSet_IsStoredCorrectly()
    {
        var ratio = new ImageTypeRatio(0.5, 0.5, 0.0, 10);
        var extraction = new AestheticExtraction([], [], [], null, ImageTypeRatio: ratio);

        Assert.NotNull(extraction.ImageTypeRatio);
        Assert.Equal(0.5, extraction.ImageTypeRatio!.Photo);
    }

    // ── CACD-PA-04-c: existing 6-param construction unchanged ────────────────

    [Fact]
    public void ExistingSixParamConstruction_NewFieldsAreNull()
    {
        IReadOnlyList<SlideBackgroundKind> pattern = [SlideBackgroundKind.Photo];
        IReadOnlyList<ColorPairing> pairings = [new ColorPairing("#AA0000", "#FFFFFF", 1)];

        var extraction = new AestheticExtraction([], ["editorial"], [], "playful", pattern, pairings);

        Assert.Null(extraction.ConceptualMethod);
        Assert.Null(extraction.ImageTypeRatio);
    }

    // ── AestheticExtraction.Empty has null for both new fields ────────────────

    [Fact]
    public void Empty_StaticProperty_NewFieldsAreNull()
    {
        var empty = AestheticExtraction.Empty;

        Assert.Null(empty.ConceptualMethod);
        Assert.Null(empty.ImageTypeRatio);
    }

    // ── with-expression on new fields compiles (CACD-PA-04-a/b) ─────────────

    [Fact]
    public void WithExpression_ConceptualMethod_CompilesAndYieldsNewValue()
    {
        var original = new AestheticExtraction([], [], [], null);
        var cm       = new ConceptualMethod(ArtDirectionMethod.LiteralIllustration, "Tender", ["watercolor"]);
        var updated  = original with { ConceptualMethod = cm };

        Assert.Null(original.ConceptualMethod);
        Assert.NotNull(updated.ConceptualMethod);
        Assert.Equal(ArtDirectionMethod.LiteralIllustration, updated.ConceptualMethod!.Method);
    }

    [Fact]
    public void WithExpression_ImageTypeRatio_CompilesAndYieldsNewValue()
    {
        var original = new AestheticExtraction([], [], [], null);
        var ratio    = new ImageTypeRatio(0.7, 0.3, 0.0, 7);
        var updated  = original with { ImageTypeRatio = ratio };

        Assert.Null(original.ImageTypeRatio);
        Assert.NotNull(updated.ImageTypeRatio);
        Assert.Equal(0.7, updated.ImageTypeRatio!.Photo);
    }

    [Fact]
    public void WithExpression_ConceptualMethod_SetToNull_CompilesAndYieldsNull()
    {
        var cm        = new ConceptualMethod(ArtDirectionMethod.MetaphoricalEditorial, "r", []);
        var withValue = new AestheticExtraction([], [], [], null, ConceptualMethod: cm);
        var cleared   = withValue with { ConceptualMethod = null };

        Assert.Null(cleared.ConceptualMethod);
    }
}
