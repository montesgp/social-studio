using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;

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
}
