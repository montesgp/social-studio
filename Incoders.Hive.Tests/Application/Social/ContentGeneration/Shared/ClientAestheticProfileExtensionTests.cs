using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// Additive extension tests for <see cref="ClientAestheticProfile"/>.
/// Covers client-aligned-carousel-generation fields (BackgroundPattern, TypicalSlideCount, ColorPairings) and
/// WU-1 task 1.11 [RED] additions (ConceptualMethod, ImageTypeRatio; CACD-PA-05).
/// Refs: design §3.1; tasks 1.5/1.6 (carousel), 1.11/1.12 (conceptual); CAC-BSP-02; CAC-BSP-04-a; CACD-PA-05.
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

    // ── WU-1 task 1.11 [RED]: ConceptualMethod field (CACD-PA-05) ────────────

    [Fact]
    public void ConceptualMethod_DefaultsToNull_WhenOmittedFromConstruction()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: ["dark minimal"],
            VoiceTone: "warm",
            AverageSlidesPerCarousel: 5.0,
            AnalyzedAtUtc: DateTime.UtcNow);

        Assert.Null(profile.ConceptualMethod);
    }

    [Fact]
    public void ConceptualMethod_WhenSet_IsStoredCorrectly()
    {
        var cm = new ConceptualMethod(ArtDirectionMethod.MetaphoricalEditorial, "Metaphor", ["mono-color backdrop"]);
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            ConceptualMethod: cm);

        Assert.NotNull(profile.ConceptualMethod);
        Assert.Equal(ArtDirectionMethod.MetaphoricalEditorial, profile.ConceptualMethod!.Method);
    }

    // ── WU-1 task 1.11 [RED]: ImageTypeRatio field (CACD-PA-05) ─────────────

    [Fact]
    public void ImageTypeRatio_DefaultsToNull_WhenOmittedFromConstruction()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null);

        Assert.Null(profile.ImageTypeRatio);
    }

    [Fact]
    public void ImageTypeRatio_WhenSet_IsStoredCorrectly()
    {
        var ratio = new ImageTypeRatio(0.5, 0.5, 0.0, 10);
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            ImageTypeRatio: ratio);

        Assert.NotNull(profile.ImageTypeRatio);
        Assert.Equal(0.5, profile.ImageTypeRatio!.Photo);
    }

    // ── with { ConceptualMethod = null } compiles and yields null ────────────

    [Fact]
    public void WithExpression_ConceptualMethod_Null_Compiles()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null);

        var updated = profile with { ConceptualMethod = null };

        Assert.Null(updated.ConceptualMethod);
    }

    [Fact]
    public void WithExpression_ConceptualMethod_NonNull_CompilesAndYieldsValue()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null);

        var cm      = new ConceptualMethod(ArtDirectionMethod.LiteralIllustration, "Tender", ["watercolor"]);
        var updated = profile with { ConceptualMethod = cm };

        Assert.NotNull(updated.ConceptualMethod);
        Assert.Equal(ArtDirectionMethod.LiteralIllustration, updated.ConceptualMethod!.Method);
    }

    // ── with { ImageTypeRatio = null } compiles and yields null ──────────────

    [Fact]
    public void WithExpression_ImageTypeRatio_Null_Compiles()
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null);

        var updated = profile with { ImageTypeRatio = null };

        Assert.Null(updated.ImageTypeRatio);
    }

    // ── Cold-start back-compat: existing construction with no new params ──────

    [Fact]
    public void ExistingConstruction_WithNoNewConceptualParams_NewFieldsAreNull()
    {
        // All construction sites from before this change compile unchanged (CACD-PA-05).
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: ["dark minimal"],
            VoiceTone: "warm",
            AverageSlidesPerCarousel: 5.0,
            AnalyzedAtUtc: DateTime.UtcNow,
            BackgroundPattern: [SlideBackgroundKind.Photo],
            TypicalSlideCount: 5,
            ColorPairings: [new ColorPairing("#111111", "#EEEEEE", 1)]);

        Assert.Null(profile.ConceptualMethod);
        Assert.Null(profile.ImageTypeRatio);
    }
}
