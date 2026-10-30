namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual.Layout;

/// <summary>
/// WU-4 (CLIF-CG-03/04/05, design.md D-C/D-D): the pure position-recompute formula for the
/// two-pass reflow loop. Operates over stacked, top-anchored slots only, in declared order — the
/// anchor slot (index 0) never moves. Monotone non-decreasing: when every slot already fits
/// (measured height &lt;= declared min-height for every preceding slot), the recomputed top for
/// every slot equals its declared top EXACTLY (CLIF-CG-08, byte-identical fast path).
/// </summary>
public static class MeasuredLayoutReflow
{
    /// <summary>Hard cap on total renders per slide (1 initial + at most 2 re-renders).</summary>
    public const int MaxRenderPasses = 3;

    /// <summary>Minimum recomputed-top delta (px) considered "material" — below this, the fast
    /// path treats a recomputed top as unchanged from the rendered top.</summary>
    public const double EpsilonPx = 1.0;

    /// <summary>Distance (px) reserved above the canvas bottom edge that reflowed content must
    /// never cross — the clamp limit is <c>canvasHeight - BottomSafePx</c>.</summary>
    public const double BottomSafePx = 140;

    /// <summary>
    /// Recomputes each slot's <c>top</c> from the preceding slot's measured extent.
    /// <c>declared</c> and <c>measured</c> MUST be supplied in the same stacked, top-anchored,
    /// declared-order slot sequence (e.g. carousel: title, subtitle; story: headline, subtitle,
    /// body). A slot present in <paramref name="declared"/> but absent from
    /// <paramref name="measured"/> falls back to its own declared height (treated as already
    /// fitting). Returns a map of slot name to the recomputed integer <c>top</c> px value for
    /// EVERY slot in <paramref name="declared"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, int> RecomputeTops(
        IReadOnlyList<SlotBox> declared,
        IReadOnlyList<SlotBox> measured,
        int canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(measured);

        var tops = new Dictionary<string, int>(StringComparer.Ordinal);
        if (declared.Count == 0)
        {
            return tops;
        }

        var measuredBySlot = new Dictionary<string, SlotBox>(StringComparer.Ordinal);
        foreach (var box in measured)
        {
            measuredBySlot[box.Slot] = box;
        }

        double prevTop = 0;
        double prevDeclaredTop = 0;
        double prevDeclaredMinHeight = 0;
        double prevEffectiveHeight = 0;
        var clampLimit = canvasHeight - BottomSafePx;

        for (var i = 0; i < declared.Count; i++)
        {
            var slot = declared[i];
            double resolvedTop;

            if (i == 0)
            {
                resolvedTop = slot.Top;
            }
            else
            {
                var gap = slot.Top - (prevDeclaredTop + prevDeclaredMinHeight);
                var prevBottom = prevTop + prevEffectiveHeight;
                resolvedTop = Math.Max(slot.Top, Math.Round(prevBottom + gap));
            }

            var effectiveHeight = Math.Max(
                slot.Height,
                measuredBySlot.TryGetValue(slot.Slot, out var m) ? m.Height : slot.Height);

            if (resolvedTop + effectiveHeight > clampLimit)
            {
                // Stop at the clamp rather than pushing text off-canvas — residual overlap (if any)
                // is reported by the caller's overlap check on the next measurement, not hidden here.
                resolvedTop = Math.Max(slot.Top, clampLimit - effectiveHeight);
            }

            tops[slot.Slot] = (int)Math.Round(resolvedTop);

            prevTop = resolvedTop;
            prevDeclaredTop = slot.Top;
            prevDeclaredMinHeight = slot.Height;
            prevEffectiveHeight = effectiveHeight;
        }

        return tops;
    }
}
