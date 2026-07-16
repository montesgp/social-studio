using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// WU-2 RED tests: CreativeSlideIndexResolver default-resolution helper.
/// All behaviour per design §0 OQ-1, OQ-2; tasks 2.3.
/// Refs: CIG-BSP-02 (1-based range), CIG-BSP-03 (deduplication, cap).
/// </summary>
public sealed class CreativeSlideIndexResolverTests
{
    // -----------------------------------------------------------------------
    // OQ-1 default: null → {first, last} (1-based)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_NullIndices_FiveSlides_ReturnsFirstAndLast()
    {
        var result = CreativeSlideIndexResolver.Resolve(null, slideCount: 5);

        Assert.Equal([1, 5], result);
    }

    [Fact]
    public void Resolve_NullIndices_SingleSlide_ReturnsOnlyFirst()
    {
        // Degenerate case: first == last → dedupe to [1]
        var result = CreativeSlideIndexResolver.Resolve(null, slideCount: 1);

        Assert.Equal([1], result);
    }

    [Fact]
    public void Resolve_EmptyList_FiveSlides_ReturnsFirstAndLast()
    {
        // Empty list treated same as null → default first+last
        var result = CreativeSlideIndexResolver.Resolve([], slideCount: 5);

        Assert.Equal([1, 5], result);
    }

    // -----------------------------------------------------------------------
    // CIG-BSP-02-c: 1-based range filter — indices outside 1..slideCount excluded
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_AllOutOfRange_ReturnsEmpty()
    {
        // 0 is below 1-based range; 9 exceeds slideCount=5
        var result = CreativeSlideIndexResolver.Resolve([0, 9], slideCount: 5);

        Assert.Empty(result);
    }

    [Fact]
    public void Resolve_MixedInRangeAndOutOfRange_ReturnsOnlyInRange()
    {
        var result = CreativeSlideIndexResolver.Resolve([0, 1, 5, 9], slideCount: 5);

        Assert.Equal([1, 5], result);
    }

    // -----------------------------------------------------------------------
    // Explicit set, all in range
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ExplicitInRangeSet_ReturnsOrderedSet()
    {
        var result = CreativeSlideIndexResolver.Resolve([1, 3], slideCount: 5);

        Assert.Equal([1, 3], result);
    }

    // -----------------------------------------------------------------------
    // Deduplication
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DuplicateIndices_Deduped()
    {
        var result = CreativeSlideIndexResolver.Resolve([1, 1, 3], slideCount: 5);

        Assert.Equal([1, 3], result);
    }

    // -----------------------------------------------------------------------
    // OQ-2 cap: MaxCreativeSlidesPerPackage = 3
    // -----------------------------------------------------------------------

    [Fact]
    public void MaxCreativeSlidesPerPackage_IsThree()
    {
        Assert.Equal(3, CreativeSlideIndexResolver.MaxCreativeSlidesPerPackage);
    }

    [Fact]
    public void Resolve_MoreThanCap_CappedToFirstThree()
    {
        var result = CreativeSlideIndexResolver.Resolve([1, 2, 3, 4], slideCount: 5);

        // Capped to first MaxCreativeSlidesPerPackage = 3
        Assert.Equal([1, 2, 3], result);
    }

    [Fact]
    public void Resolve_MoreThanCapIndices_TakesOnlyFirstCap()
    {
        // After dedup and range filter, [1, 2, 3, 4] → take 3
        var result = CreativeSlideIndexResolver.Resolve([1, 2, 3, 4], slideCount: 5);

        Assert.Equal(CreativeSlideIndexResolver.MaxCreativeSlidesPerPackage, result.Count);
    }

    // -----------------------------------------------------------------------
    // Edge: slideCount = 2 with null → [1, 2]
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_NullIndices_TwoSlides_ReturnsBoth()
    {
        var result = CreativeSlideIndexResolver.Resolve(null, slideCount: 2);

        Assert.Equal([1, 2], result);
    }

    // -----------------------------------------------------------------------
    // Ordering is preserved (first occurrence kept after dedup, ascending stable)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_UnsortedInput_DedupsAndPreservesFirstOccurrence()
    {
        // The resolver applies dedup; order is input-order stable
        var result = CreativeSlideIndexResolver.Resolve([3, 1, 4, 3, 2], slideCount: 5);

        // After dedup: [3, 1, 4, 2] — take first MaxCreativeSlidesPerPackage = 3 → [3, 1, 4]
        Assert.Equal(3, result.Count);
        Assert.Equal(3, result[0]);
        Assert.Equal(1, result[1]);
        Assert.Equal(4, result[2]);
    }

    // -----------------------------------------------------------------------
    // ResolveFromPattern — WU-2 (CAC-BSP-01, CAC-BSP-02, CAC-BSP-04; design §3.2, §0 OQ-1)
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveFromPattern_NullPattern_FiveSlides_DelegatesToDefaultFirstAndLast()
    {
        // Null pattern → same as Resolve(null, 5) → {first, last}
        var result = CreativeSlideIndexResolver.ResolveFromPattern(null, slideCount: 5);

        Assert.Equal([1, 5], result);
    }

    [Fact]
    public void ResolveFromPattern_EmptyPattern_FiveSlides_DelegatesToDefaultFirstAndLast()
    {
        // Empty pattern → same as null → {first, last}
        var result = CreativeSlideIndexResolver.ResolveFromPattern([], slideCount: 5);

        Assert.Equal([1, 5], result);
    }

    [Fact]
    public void ResolveFromPattern_EqualLength_PhotoFlatColorInterleaved_ReturnsPhotoPositionsCapped()
    {
        // Pattern [Photo,FlatColor,Photo,FlatColor,Photo] (count=5), slideCount=5 → 1:1 mapping.
        // Photo positions (0-based): 0,2,4 → 1-based slides: 1,3,5 → within cap (MaxCreativeSlidesPerPackage=3).
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.Photo,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo
        ];

        var result = CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount: 5);

        Assert.Equal([1, 3, 5], result);
    }

    [Fact]
    public void ResolveFromPattern_ShorterPattern_ProportionalMapping_CappedToMax()
    {
        // Pattern [Photo,FlatColor,Photo] (count=3), slideCount=5.
        // Proportional index = (i-1)*3/5 (integer floor):
        //   i=1 → 0 → Photo  → creative
        //   i=2 → 0 → Photo  → creative
        //   i=3 → 1 → FlatColor → skip
        //   i=4 → 1 → FlatColor → skip
        //   i=5 → 2 → Photo  → creative
        // Photo slides [1,2,5], within MaxCreativeSlidesPerPackage=3 → [1,2,5].
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.Photo,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo
        ];

        var result = CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount: 5);

        Assert.Equal([1, 2, 5], result);
    }

    [Fact]
    public void ResolveFromPattern_AllPhotoPattern_CappedToMax()
    {
        // All Photo → every slide is creative → capped to MaxCreativeSlidesPerPackage.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.Photo,
            SlideBackgroundKind.Photo,
            SlideBackgroundKind.Photo
        ];

        var result = CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount: 5);

        Assert.Equal(CreativeSlideIndexResolver.MaxCreativeSlidesPerPackage, result.Count);
        Assert.Equal(1, result[0]);
        Assert.Equal(2, result[1]);
        Assert.Equal(3, result[2]);
    }

    // -----------------------------------------------------------------------
    // ResolveKindForSlide — task 1.1 (FFS-CG-01). Same proportional mapping as
    // ResolveFromPattern (patternIndex = (slideIndex-1)*patternCount/slideCount),
    // but returns the mapped kind for ONE slide instead of filtering for Photo.
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveKindForSlide_NullPattern_ReturnsNull()
    {
        var result = CreativeSlideIndexResolver.ResolveKindForSlide(null, slideCount: 5, slideIndex: 1);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveKindForSlide_EmptyPattern_ReturnsNull()
    {
        var result = CreativeSlideIndexResolver.ResolveKindForSlide([], slideCount: 5, slideIndex: 1);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveKindForSlide_EqualLength_ReturnsMappedKindForEachIndex()
    {
        // Pattern [Photo,FlatColor,Photo,FlatColor,Photo] (count=5), slideCount=5 → 1:1 mapping.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.Photo,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo
        ];

        Assert.Equal(SlideBackgroundKind.Photo, CreativeSlideIndexResolver.ResolveKindForSlide(pattern, 5, 1));
        Assert.Equal(SlideBackgroundKind.FlatColor, CreativeSlideIndexResolver.ResolveKindForSlide(pattern, 5, 2));
        Assert.Equal(SlideBackgroundKind.Photo, CreativeSlideIndexResolver.ResolveKindForSlide(pattern, 5, 3));
        Assert.Equal(SlideBackgroundKind.FlatColor, CreativeSlideIndexResolver.ResolveKindForSlide(pattern, 5, 4));
        Assert.Equal(SlideBackgroundKind.Photo, CreativeSlideIndexResolver.ResolveKindForSlide(pattern, 5, 5));
    }

    [Fact]
    public void ResolveKindForSlide_ShorterPattern_ProportionalMapping_MatchesResolveFromPattern()
    {
        // Pattern [Photo,FlatColor,Photo] (count=3), slideCount=5 — same fixture as
        // ResolveFromPattern_ShorterPattern_ProportionalMapping_CappedToMax above:
        //   i=1 → patternIndex 0 → Photo
        //   i=2 → patternIndex 0 → Photo
        //   i=3 → patternIndex 1 → FlatColor
        //   i=4 → patternIndex 1 → FlatColor
        //   i=5 → patternIndex 2 → Photo
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.Photo,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo
        ];

        Assert.Equal(SlideBackgroundKind.Photo, CreativeSlideIndexResolver.ResolveKindForSlide(pattern, 5, 1));
        Assert.Equal(SlideBackgroundKind.Photo, CreativeSlideIndexResolver.ResolveKindForSlide(pattern, 5, 2));
        Assert.Equal(SlideBackgroundKind.FlatColor, CreativeSlideIndexResolver.ResolveKindForSlide(pattern, 5, 3));
        Assert.Equal(SlideBackgroundKind.FlatColor, CreativeSlideIndexResolver.ResolveKindForSlide(pattern, 5, 4));
        Assert.Equal(SlideBackgroundKind.Photo, CreativeSlideIndexResolver.ResolveKindForSlide(pattern, 5, 5));
    }

    [Fact]
    public void ResolveKindForSlide_AllFlatColorPattern_ReturnsFlatColorForEverySlide()
    {
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor
        ];

        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal(SlideBackgroundKind.FlatColor, CreativeSlideIndexResolver.ResolveKindForSlide(pattern, 4, i));
        }
    }

    // -----------------------------------------------------------------------
    // Photo-ratio floor (client-aligned-carousel-generation follow-up):
    // when the corpus photo ratio is meaningful, an all-flat pattern must still
    // yield at least one generated-image slide (magnitude overrides order).
    // -----------------------------------------------------------------------

    [Fact]
    public void MinPhotoRatioForFloor_IsThirtyPercent()
    {
        Assert.Equal(0.30, CreativeSlideIndexResolver.MinPhotoRatioForFloor);
    }

    [Fact]
    public void ResolveFromPattern_AllFlatPattern_WithMeaningfulPhotoRatio_FloorsToOnePhotoSlide()
    {
        // Reproduces the real "Comet Comedy" case: pattern all-FlatColor, photoRatio 0.357, 3 slides.
        // Without the floor this returns [] (no image backgrounds); with it, at least slide 1 is creative.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor
        ];

        var result = CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount: 3, photoRatio: 0.357);

        Assert.Equal([1], result);
    }

    [Fact]
    public void ResolveFromPattern_AllFlatPattern_NullRatio_KeepsPatternOnlyBehaviorEmpty()
    {
        // Cold-start / no ratio → byte-identical to before (no floor): all-flat → empty.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor
        ];

        var result = CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount: 3, photoRatio: null);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveFromPattern_AllFlatPattern_SubThresholdRatio_NoFloor()
    {
        // Genuinely flat-dominant client (ratio below threshold) → no floor, stays empty.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor
        ];

        var result = CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount: 3, photoRatio: 0.10);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveFromPattern_AllFlatPattern_HighRatio_FloorsProportionally_SpreadFromEdges()
    {
        // ratio 0.7 * 5 = 3.5 → 4, capped to MaxCreativeSlidesPerPackage=3. Spread from edges → {1,5,2} sorted.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor
        ];

        var result = CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount: 5, photoRatio: 0.7);

        Assert.Equal([1, 2, 5], result);
    }

    [Fact]
    public void ResolveFromPattern_PatternAlreadyMeetsTarget_RatioDoesNotAddOrReorder()
    {
        // Pattern already has one photo slide (slide 3); ratio 0.357*3≈1 target already met → no top-up.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo
        ];

        var result = CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount: 3, photoRatio: 0.357);

        Assert.Equal([3], result);
    }

    [Fact]
    public void ResolveFromPattern_RatioFloor_NeverDropsPatternPhotoSlides()
    {
        // Pattern marks slide 5 as photo; a modest ratio floor must ADD (not replace) → slide 5 preserved.
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo
        ];

        var result = CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount: 5, photoRatio: 0.5);

        // target = round(0.5*5 away)=3; base=[5]; top-up edges → add 1, then 2 → {1,2,5}.
        Assert.Contains(5, result);
        Assert.Equal([1, 2, 5], result);
    }
}
