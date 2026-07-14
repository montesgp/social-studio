using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Background;

/// <summary>
/// Resolves the set of 1-based creative slide indices for a ClientAesthetic carousel.
/// Applies the OQ-1 default (first + last), 1-based range filter, deduplication,
/// and the OQ-2 cap (<see cref="MaxCreativeSlidesPerPackage"/>).
/// Refs: design §0 OQ-1, OQ-2; tasks 2.4.
/// </summary>
public static class CreativeSlideIndexResolver
{
    /// <summary>
    /// Maximum number of creative slides that will receive a generated raster background per package.
    /// Owner-confirmed value (OQ-2): 3 — covers carousels whose captured pattern has up to three Photo
    /// slides (e.g. [Photo, Photo, FlatColor, FlatColor, Photo] → slides 1, 2, 5) without unbounded
    /// generation time/GPU cost. This constant is the sole authoritative cap.
    /// </summary>
    public const int MaxCreativeSlidesPerPackage = 3;

    /// <summary>
    /// Resolves the final set of creative slide indices for <paramref name="slideCount"/> slides.
    /// <list type="bullet">
    ///   <item><description>Null or empty → default to { 1, slideCount } (first + last, deduped for single-slide case).</description></item>
    ///   <item><description>1-based range filter: indices outside 1..<paramref name="slideCount"/> are silently excluded.</description></item>
    ///   <item><description>Deduplication: first occurrence wins; order is preserved.</description></item>
    ///   <item><description>Cap: at most <see cref="MaxCreativeSlidesPerPackage"/> indices returned (first N after dedup).</description></item>
    /// </list>
    /// </summary>
    /// <param name="indices">Raw 1-based indices from <c>ClientAestheticProfile.CreativeSlideIndices</c>. May be null or empty.</param>
    /// <param name="slideCount">Total number of slides in the carousel (1-based upper bound).</param>
    /// <returns>Resolved, filtered, deduped, capped list of 1-based slide indices.</returns>
    public static IReadOnlyList<int> Resolve(IReadOnlyList<int>? indices, int slideCount)
    {
        IEnumerable<int> source;

        if (indices is null || indices.Count == 0)
        {
            // OQ-1 default: first + last; dedup handles the single-slide case (1 == slideCount)
            source = [1, slideCount];
        }
        else
        {
            source = indices;
        }

        return source
            .Where(i => i >= 1 && i <= slideCount)    // 1-based range filter
            .Distinct()                                 // deduplication (LINQ Distinct preserves first occurrence)
            .Take(MaxCreativeSlidesPerPackage)          // OQ-2 cap
            .ToList();
    }

    /// <summary>
    /// Resolves the set of 1-based creative slide indices from a captured background-kind pattern.
    /// Maps the pattern onto the actual slide count by proportional scaling and returns
    /// the indices whose mapped kind is <see cref="SlideBackgroundKind.Photo"/>,
    /// deduped and capped to <see cref="MaxCreativeSlidesPerPackage"/>.
    /// Refs: design §3.2, §0 OQ-1; CAC-BSP-01, CAC-BSP-02, CAC-BSP-04.
    /// </summary>
    /// <param name="pattern">
    /// Ordered per-slide background-kind sequence from <c>ClientAestheticProfile.BackgroundPattern</c>.
    /// Null or empty → delegates to the existing <see cref="Resolve"/> default ({first, last}).
    /// </param>
    /// <param name="slideCount">Total number of slides in the carousel (1-based upper bound).</param>
    /// <returns>Resolved, deduped, capped list of 1-based creative slide indices.</returns>
    public static IReadOnlyList<int> ResolveFromPattern(
        IReadOnlyList<SlideBackgroundKind>? pattern, int slideCount)
    {
        if (pattern is null || pattern.Count == 0)
            return Resolve(null, slideCount);

        var patternCount = pattern.Count;
        var photoIndices = new List<int>();

        for (var i = 1; i <= slideCount; i++)
        {
            // Proportional index: floor((i-1) * patternCount / slideCount).
            // Integer arithmetic in C# truncates toward zero, which equals floor for positive operands.
            // Equal-length case (patternCount == slideCount) degenerates to 1:1 (patternIndex == i-1).
            var patternIndex = (i - 1) * patternCount / slideCount;

            if (pattern[patternIndex] == SlideBackgroundKind.Photo)
                photoIndices.Add(i);
        }

        // Slide indices are unique (i is strictly monotone), so Distinct() is precautionary.
        return photoIndices
            .Distinct()
            .Take(MaxCreativeSlidesPerPackage)
            .ToList();
    }

    /// <summary>
    /// Resolves the mapped <see cref="SlideBackgroundKind"/> for a SINGLE 1-based slide index,
    /// using the SAME proportional mapping formula as <see cref="ResolveFromPattern"/>
    /// (<c>patternIndex = (slideIndex-1) * patternCount / slideCount</c>).
    /// Refs: design C1; FFS-CG-01.
    /// </summary>
    /// <param name="pattern">
    /// Ordered per-slide background-kind sequence from <c>ClientAestheticProfile.BackgroundPattern</c>.
    /// Null or empty → <c>null</c> (no mapped kind — caller must NOT short-circuit).
    /// </param>
    /// <param name="slideCount">Total number of slides in the carousel (1-based upper bound).</param>
    /// <param name="slideIndex">The 1-based slide index to resolve.</param>
    /// <returns>The mapped kind, or <c>null</c> when the pattern is absent.</returns>
    public static SlideBackgroundKind? ResolveKindForSlide(
        IReadOnlyList<SlideBackgroundKind>? pattern, int slideCount, int slideIndex)
    {
        if (pattern is null || pattern.Count == 0)
            return null;

        var patternCount = pattern.Count;
        // Same proportional formula as ResolveFromPattern; integer truncation equals floor here
        // because slideIndex, patternCount, slideCount are all positive.
        var patternIndex = (slideIndex - 1) * patternCount / slideCount;
        return pattern[patternIndex];
    }
}
