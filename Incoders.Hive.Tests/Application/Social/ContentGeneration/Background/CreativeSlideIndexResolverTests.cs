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
    // OQ-2 cap: MaxCreativeSlidesPerPackage = 2
    // -----------------------------------------------------------------------

    [Fact]
    public void MaxCreativeSlidesPerPackage_IsTwo()
    {
        Assert.Equal(2, CreativeSlideIndexResolver.MaxCreativeSlidesPerPackage);
    }

    [Fact]
    public void Resolve_MoreThanCap_CappedToFirstTwo()
    {
        var result = CreativeSlideIndexResolver.Resolve([1, 2, 3], slideCount: 5);

        // Capped to first MaxCreativeSlidesPerPackage = 2
        Assert.Equal([1, 2], result);
    }

    [Fact]
    public void Resolve_ThreeIndices_TakesOnlyFirstTwo()
    {
        // After dedup and range filter, [1, 2, 3] → take 2
        var result = CreativeSlideIndexResolver.Resolve([1, 2, 3], slideCount: 5);

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
        var result = CreativeSlideIndexResolver.Resolve([3, 1, 3, 2], slideCount: 5);

        // After dedup: [3, 1, 2] — take first 2 → [3, 1]
        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0]);
        Assert.Equal(1, result[1]);
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
        // Photo positions (0-based): 0,2,4 → 1-based slides: 1,3,5 → capped to [1,3] (MaxCreativeSlidesPerPackage=2).
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.Photo,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo
        ];

        var result = CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount: 5);

        Assert.Equal([1, 3], result);
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
        // Photo slides [1,2,5], capped to first MaxCreativeSlidesPerPackage=2 → [1,2].
        IReadOnlyList<SlideBackgroundKind> pattern =
        [
            SlideBackgroundKind.Photo,
            SlideBackgroundKind.FlatColor,
            SlideBackgroundKind.Photo
        ];

        var result = CreativeSlideIndexResolver.ResolveFromPattern(pattern, slideCount: 5);

        Assert.Equal([1, 2], result);
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
}
