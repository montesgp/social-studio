namespace SocialStudio.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Reconciles the extracted per-slide <see cref="SlideBackgroundTransparency"/> signal against the
/// length of <c>BackgroundPattern</c> (the ORDER signal it is index-aligned and parallel to), so the
/// two arrays can never drift in the persisted aesthetic profile.
///
/// <para>
/// client-aesthetic-fidelity-fixes (WU-3a, design D3; CAFF-PA-04). Modelled on
/// <see cref="BackgroundPatternReconciler"/>'s tolerant contract: it runs at the extractor
/// <c>Map()</c> site, NEVER throws, and NEVER mutates <c>BackgroundPattern</c> itself.
/// </para>
/// </summary>
public static class BackgroundTransparencyReconciler
{
    /// <summary>
    /// Index-aligns <paramref name="transparency"/> to <paramref name="pattern"/>'s length: pads any
    /// missing trailing index with <c>null</c> (unmeasured) when <paramref name="transparency"/> is
    /// shorter, and truncates surplus entries when it is longer. Returns <c>null</c> when
    /// <paramref name="pattern"/> is null/empty (nothing to align against) or when
    /// <paramref name="transparency"/> is null/empty (never measured at all — cold-start parity).
    /// </summary>
    public static IReadOnlyList<SlideBackgroundTransparency?>? Align(
        IReadOnlyList<SlideBackgroundKind>? pattern,
        IReadOnlyList<SlideBackgroundTransparency?>? transparency)
    {
        if (pattern is null || pattern.Count == 0 || transparency is null || transparency.Count == 0)
        {
            return null;
        }

        var result = new SlideBackgroundTransparency?[pattern.Count];
        for (var i = 0; i < pattern.Count; i++)
        {
            result[i] = i < transparency.Count ? transparency[i] : null;
        }

        return result;
    }
}
