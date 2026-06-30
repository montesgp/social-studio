using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-1 task 1.5 [RED] — <see cref="ClientAestheticProfile"/> additive extension for the three
/// client-aligned-carousel-generation capture fields.
/// Asserts: existing construction unchanged; new fields nullable and accessible via <c>with</c>.
/// Refs: design §3.1; tasks 1.5/1.6; CAC-BSP-02, CAC-BSP-04-a.
/// </summary>
public sealed class ClientAestheticProfileExtensionTests
{
    // ── Back-compat: existing construction with no new params compiles ─────────

    [Fact]
    public void ExistingConstruction_WithNoNewParams_CompilesAndNewFieldsAreNull()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: ["dark minimal"],
            VoiceTone: "warm",
            AverageSlidesPerCarousel: 5.0,
            AnalyzedAtUtc: DateTime.UtcNow);

        // Cold-start back-compat (CAC-BSP-04-a shape check)
        Assert.Null(profile.BackgroundPattern);
        Assert.Null(profile.TypicalSlideCount);
        Assert.Null(profile.ColorPairings);
    }

    // ── with { BackgroundPattern = null } compiles and yields null ─────────────

    [Fact]
    public void WithExpression_BackgroundPattern_Null_Compiles()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null);

        var updated = profile with { BackgroundPattern = null };

        Assert.Null(updated.BackgroundPattern);
    }

    // ── with { BackgroundPattern = [...] } yields the assigned value ───────────

    [Fact]
    public void WithExpression_BackgroundPattern_NonNull_Compiles()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null);

        IReadOnlyList<SlideBackgroundKind> pattern = [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor];
        var updated = profile with { BackgroundPattern = pattern };

        Assert.NotNull(updated.BackgroundPattern);
        Assert.Equal(2, updated.BackgroundPattern!.Count);
        Assert.Equal(SlideBackgroundKind.Photo, updated.BackgroundPattern[0]);
        Assert.Equal(SlideBackgroundKind.FlatColor, updated.BackgroundPattern[1]);
    }

    // ── with { TypicalSlideCount = 5 } compiles and yields 5 ─────────────────

    [Fact]
    public void WithExpression_TypicalSlideCount_CompilesAndYieldsValue()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null);

        var updated = profile with { TypicalSlideCount = 5 };

        Assert.Equal(5, updated.TypicalSlideCount);
    }

    // ── with { ColorPairings = [] } compiles and yields empty list ─────────────

    [Fact]
    public void WithExpression_ColorPairings_EmptyList_CompilesAndYieldsEmptyList()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null);

        var updated = profile with { ColorPairings = [] };

        Assert.NotNull(updated.ColorPairings);
        Assert.Empty(updated.ColorPairings!);
    }

    // ── with { ColorPairings = [...] } compiles and yields the assigned list ───

    [Fact]
    public void WithExpression_ColorPairings_NonNull_Compiles()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null);

        IReadOnlyList<ColorPairing> pairings = [new ColorPairing("#1A2B3C", "#FFFFFF", 1)];
        var updated = profile with { ColorPairings = pairings };

        Assert.NotNull(updated.ColorPairings);
        Assert.Single(updated.ColorPairings!);
        Assert.Equal("#1A2B3C", updated.ColorPairings![0].BackgroundHex);
    }

    // ── Explicit construction with new fields ──────────────────────────────────

    [Fact]
    public void ExplicitConstruction_WithAllNewFields_StoresCorrectly()
    {
        IReadOnlyList<SlideBackgroundKind> pattern = [SlideBackgroundKind.Photo, SlideBackgroundKind.FlatColor, SlideBackgroundKind.Photo];
        IReadOnlyList<ColorPairing> pairings = [new ColorPairing("#111111", "#EEEEEE", 1)];

        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            BackgroundPattern: pattern,
            TypicalSlideCount: 3,
            ColorPairings: pairings);

        Assert.Equal(3, profile.BackgroundPattern!.Count);
        Assert.Equal(3, profile.TypicalSlideCount);
        Assert.Single(profile.ColorPairings!);
    }

    // ── Existing optional fields from prior phases still compile ───────────────

    [Fact]
    public void PriorPhaseOptionalFields_StillCompileUnchanged()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            CreativeSlideIndices: [1, 3]);

        Assert.Null(profile.BackgroundPattern);
        Assert.Null(profile.TypicalSlideCount);
        Assert.Null(profile.ColorPairings);
        Assert.Equal([1, 3], profile.CreativeSlideIndices);
    }
}
