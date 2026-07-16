namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Reconciles a client's per-slide <c>BackgroundPattern</c> (ORDER — which slides look photographic)
/// with the corpus <see cref="ImageTypeRatio"/> (MAGNITUDE — how many slides are photographic across ALL
/// analyzed posts) at analysis time, so the two signals can never contradict each other in the persisted
/// aesthetic profile.
///
/// <para>
/// Root-cause motivation: the multimodal extractor produces the ordered <c>slideMixPattern</c> as a
/// free-form judgement while it counts <c>imageTypeCounts</c> separately. The counts are a reliable
/// counting task; the ordered pattern is not. For an image-heavy client the model can emit an all-flat
/// pattern (0 photo slides) even though its own counts say a meaningful share of the client's real posts
/// are photographic. That contradiction silently suppressed ALL image generation and produced flat CSS
/// carousels that looked nothing like the client. This reconciler trusts the counts: when the pattern
/// under-represents photos versus the ratio, it injects the proportional number of photo slides.
/// </para>
///
/// <para>
/// Contract: only ever ADDS photo slides to reach the ratio-implied count; never removes a photo slide the
/// pattern already carries, never reorders the non-affected slots. A null/empty pattern or a null/sub-
/// threshold ratio is returned unchanged (cold-start parity). This mirrors the generation-time safety net
/// in <see cref="Background.CreativeSlideIndexResolver"/> so the two layers agree on when a client counts
/// as image-using.
/// </para>
/// </summary>
public static class BackgroundPatternReconciler
{
    /// <summary>
    /// Minimum corpus photo proportion (<see cref="ImageTypeRatio.Photo"/>) at or above which the
    /// persisted pattern MUST carry at least one photo slide. Kept equal in spirit to
    /// <see cref="Background.CreativeSlideIndexResolver.MinPhotoRatioForFloor"/>.
    /// </summary>
    public const double MinPhotoRatio = 0.30;

    /// <summary>
    /// Returns a pattern whose photo count is at least the ratio-implied proportional count, injecting
    /// photo slides spread from the edges inward (first, last, then inner) into non-photo slots. Returns
    /// the input unchanged when there is nothing to reconcile.
    /// </summary>
    public static IReadOnlyList<SlideBackgroundKind>? Reconcile(
        IReadOnlyList<SlideBackgroundKind>? pattern,
        ImageTypeRatio? ratio)
    {
        if (pattern is null || pattern.Count == 0 || ratio is null || ratio.Photo < MinPhotoRatio)
        {
            return pattern;
        }

        // Proportional magnitude, at least one, never exceeding the pattern length. AwayFromZero keeps the
        // count generous (image-heavy clients were under-served), e.g. 0.357 * 3 = 1.07 → 1; 0.5 * 4 = 2.
        var target = Math.Clamp(
            (int)Math.Round(ratio.Photo * pattern.Count, MidpointRounding.AwayFromZero),
            1,
            pattern.Count);

        var currentPhotos = pattern.Count(k => k == SlideBackgroundKind.Photo);
        if (currentPhotos >= target)
        {
            return pattern;
        }

        var result = pattern.ToArray();
        var remaining = target - currentPhotos;
        foreach (var index in EdgeInwardIndices(result.Length))
        {
            if (remaining == 0)
            {
                break;
            }

            if (result[index] != SlideBackgroundKind.Photo)
            {
                result[index] = SlideBackgroundKind.Photo;
                remaining--;
            }
        }

        return result;
    }

    /// <summary>
    /// Yields 0-based slot positions from the edges inward: first, last, then the inner slots ascending.
    /// Mirrors the {first, last} default so injected photo slides stay spread out across the carousel.
    /// </summary>
    private static IEnumerable<int> EdgeInwardIndices(int count)
    {
        if (count < 1)
        {
            yield break;
        }

        yield return 0;
        if (count > 1)
        {
            yield return count - 1;
        }

        for (var i = 1; i < count - 1; i++)
        {
            yield return i;
        }
    }
}
