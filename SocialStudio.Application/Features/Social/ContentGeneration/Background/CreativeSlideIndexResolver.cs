using SocialStudio.Application.Features.Social.ContentGeneration.Shared;

namespace SocialStudio.Application.Features.Social.ContentGeneration.Background;

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
    /// Maps the pattern onto the actual slide count by proportional scaling to obtain a set of candidate
    /// photo POSITIONS, then — when <paramref name="photoRatio"/> is known — reconciles that candidate set
    /// against the ratio-implied COUNT (see <paramref name="photoRatio"/>). Deduped and capped to
    /// <see cref="MaxCreativeSlidesPerPackage"/>.
    /// Refs: design §3.2, §0 OQ-1; CAC-BSP-01, CAC-BSP-02, CAC-BSP-04.
    /// </summary>
    /// <param name="pattern">
    /// Ordered per-slide background-kind sequence from <c>ClientAestheticProfile.BackgroundPattern</c>.
    /// Null or empty → delegates to the existing <see cref="Resolve"/> default ({first, last}) as the
    /// candidate position set.
    /// </param>
    /// <param name="slideCount">Total number of slides in the carousel (1-based upper bound).</param>
    /// <param name="photoRatio">
    /// Corpus photo proportion (<c>ImageTypeRatio.Photo</c>), when known.
    /// <list type="bullet">
    /// <item><b>Null</b> (cold-start / old blobs) → pattern-only behaviour, byte-identical to before: the
    /// pattern-derived candidate positions are returned as-is (deduped, capped).</item>
    /// <item><b>Non-null</b> → AUTHORITATIVE for the COUNT: <c>target = Clamp(Round(ratio * slideCount,
    /// AwayFromZero), 0, Min(MaxCreativeSlidesPerPackage, slideCount))</c>. The candidate positions are
    /// preferred for WHERE (they carry the client's ordering intent): if there are more candidates than
    /// <paramref name="photoRatio"/> implies, only the first <c>target</c> (ascending) are kept; if there
    /// are fewer, the set is topped up using the edge-inward candidate order (first, last, then inner
    /// ascending). The lower bound is 0 — a client whose corpus has no photos gets no creative slides, and
    /// a pattern-derived count can now go DOWN as well as up. This is the owner-approved replacement for
    /// the former 0.30 floor threshold: there is no cliff, the ratio scales the count proportionally at
    /// every magnitude.</item>
    /// </list>
    /// </param>
    /// <returns>Resolved, deduped, capped list of 1-based creative slide indices.</returns>
    public static IReadOnlyList<int> ResolveFromPattern(
        IReadOnlyList<SlideBackgroundKind>? pattern, int slideCount, double? photoRatio = null)
    {
        List<int> photoIndices;

        if (pattern is null || pattern.Count == 0)
        {
            photoIndices = Resolve(null, slideCount).ToList();
        }
        else
        {
            var patternCount = pattern.Count;
            photoIndices = [];

            for (var i = 1; i <= slideCount; i++)
            {
                // Proportional index: floor((i-1) * patternCount / slideCount).
                // Integer arithmetic in C# truncates toward zero, which equals floor for positive operands.
                // Equal-length case (patternCount == slideCount) degenerates to 1:1 (patternIndex == i-1).
                var patternIndex = (i - 1) * patternCount / slideCount;

                if (pattern[patternIndex] == SlideBackgroundKind.Photo)
                    photoIndices.Add(i);
            }
        }

        if (photoRatio is null)
        {
            // Cold-start parity: no ratio signal → pattern-only behaviour, byte-identical to before.
            return photoIndices
                .Distinct()
                .Take(MaxCreativeSlidesPerPackage)
                .OrderBy(i => i)
                .ToList();
        }

        // Ratio-authoritative count (owner-approved rule change: the measured ratio determines HOW MANY;
        // the pattern only determines WHERE). Replaces the former MinPhotoRatioForFloor cliff — the count
        // now scales proportionally from 0 and can go down relative to the pattern, not only up.
        return ReconcileToRatioTarget(photoIndices, slideCount, photoRatio.Value)
            .Distinct()
            .Take(MaxCreativeSlidesPerPackage)
            .OrderBy(i => i)
            .ToList();
    }

    /// <summary>
    /// Reconciles the pattern-derived candidate photo positions against the ratio-implied target COUNT.
    /// If there are more candidates than the target, keeps the first <c>target</c> in ascending order
    /// (the count goes DOWN — intentional per the owner rule). If there are fewer, tops up using the
    /// edge-inward candidate order (first, last, then inner ascending) until the target is reached.
    /// The target's lower bound is 0: a ratio of 0 yields zero creative slides.
    /// </summary>
    private static List<int> ReconcileToRatioTarget(List<int> candidateIndices, int slideCount, double photoRatio)
    {
        if (slideCount < 1)
        {
            return candidateIndices;
        }

        var target = Math.Clamp(
            (int)Math.Round(photoRatio * slideCount, MidpointRounding.AwayFromZero),
            0,
            Math.Min(MaxCreativeSlidesPerPackage, slideCount));

        var ordered = candidateIndices.Distinct().OrderBy(i => i).ToList();

        if (ordered.Count >= target)
        {
            return ordered.Take(target).ToList();
        }

        var result = new List<int>(ordered);
        var present = new HashSet<int>(ordered);
        foreach (var candidate in EdgeInwardCandidates(slideCount))
        {
            if (result.Count >= target)
            {
                break;
            }

            if (present.Add(candidate))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    /// <summary>
    /// Yields 1-based slide indices ordered from the edges inward: first, last, then the remaining inner
    /// slides ascending. Mirrors the {first, last} default so floored creative slides stay spread out.
    /// </summary>
    private static IEnumerable<int> EdgeInwardCandidates(int slideCount)
    {
        if (slideCount < 1)
        {
            yield break;
        }

        yield return 1;
        if (slideCount > 1)
        {
            yield return slideCount;
        }

        for (var i = 2; i < slideCount; i++)
        {
            yield return i;
        }
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

    /// <summary>
    /// Resolves the per-slide transparency signal for a SINGLE 1-based slide index, using the
    /// IDENTICAL proportional mapping formula as <see cref="ResolveKindForSlide"/>
    /// (<c>patternIndex = (slideIndex-1) * transparencyCount / slideCount</c>), so the transparency
    /// and background-kind signals can never drift for a carousel whose length differs from the
    /// analyzed corpus.
    /// Refs: design §3 D3; CAFF-CG-05, CAFF-CG-06.
    /// </summary>
    /// <param name="transparency">
    /// Ordered per-slide transparency sequence from <c>ClientAestheticProfile.BackgroundTransparency</c>
    /// (or the design context's mirrored field). Null or empty → <c>null</c> (no signal — caller
    /// preserves today's unconditional <c>object-fit:cover</c>).
    /// </param>
    /// <param name="slideCount">Total number of slides in the carousel (1-based upper bound).</param>
    /// <param name="slideIndex">The 1-based slide index to resolve.</param>
    /// <returns>The mapped entry (which may itself be <c>null</c> for an unmeasured slide), or <c>null</c> when the list is absent.</returns>
    public static SlideBackgroundTransparency? ResolveTransparencyForSlide(
        IReadOnlyList<SlideBackgroundTransparency?>? transparency, int slideCount, int slideIndex)
    {
        if (transparency is null || transparency.Count == 0)
            return null;

        var transparencyCount = transparency.Count;
        // Same proportional formula as ResolveKindForSlide/ResolveFromPattern.
        var patternIndex = (slideIndex - 1) * transparencyCount / slideCount;
        return transparency[patternIndex];
    }
}
