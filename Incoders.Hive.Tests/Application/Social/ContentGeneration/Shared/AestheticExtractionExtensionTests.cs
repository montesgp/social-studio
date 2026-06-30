using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-1 task 1.7 [RED] — <see cref="AestheticExtraction"/> additive extension for
/// <c>BackgroundPattern</c> and <c>ColorPairings</c>.
/// Asserts: new fields exist; null-safe construction; existing construction sites compile unchanged.
/// Refs: design §3.1; tasks 1.7/1.8; CAC-BSP-01-b shape check.
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
}
