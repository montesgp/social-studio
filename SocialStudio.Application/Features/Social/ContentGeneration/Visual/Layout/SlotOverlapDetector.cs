namespace SocialStudio.Application.Features.Social.ContentGeneration.Visual.Layout;

/// <summary>
/// A measured (or declared) rectangle for a single named text slot, in the same pixel
/// coordinate space as the rendered composition.
/// </summary>
public readonly record struct SlotBox(string Slot, double Left, double Top, double Width, double Height)
{
    /// <summary>Bottom edge (<see cref="Top"/> + <see cref="Height"/>).</summary>
    public double Bottom => Top + Height;

    /// <summary>Right edge (<see cref="Left"/> + <see cref="Width"/>).</summary>
    public double Right => Left + Width;
}

/// <summary>
/// A detected cross-slot overlap between two rectangles. <see cref="UpperSlot"/> is the box
/// rendered higher on the canvas (smaller <c>Top</c>); <see cref="LowerSlot"/> is the box it
/// intrudes into.
/// </summary>
public sealed record SlotOverlap(string UpperSlot, string LowerSlot, double IntersectionAreaPx2);

/// <summary>
/// Pure, pairwise cross-slot overlap detection over measured (or declared) slot geometry.
/// WU-2 — see design.md D-A / Interfaces block. Independent of, and additional to, the
/// existing single-slot <c>VisualGuardrailValidator.ValidateOverflow</c> char-count heuristic,
/// which this type does not touch.
/// </summary>
public static class SlotOverlapDetector
{
    /// <summary>
    /// Minimum per-axis intersection (pixels) required for a pair to count as overlapping.
    /// A pair overlaps only when BOTH axes intersect by MORE than this tolerance — a
    /// vertical-band-only test would false-positive on side-by-side slots.
    /// </summary>
    public const double TolerancePx = 1.0;

    /// <summary>
    /// Detects every pairwise overlap among <paramref name="boxes"/>. Pairs are emitted once
    /// (never both (A,B) and (B,A)), ordered by the upper slot's <c>Top</c> then its
    /// <c>Slot</c> name for determinism regardless of input order.
    /// </summary>
    public static IReadOnlyList<SlotOverlap> Detect(IReadOnlyList<SlotBox> boxes)
    {
        var overlaps = new List<(double UpperTop, SlotOverlap Overlap)>();

        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i];
                var b = boxes[j];

                var overlapWidth = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                var overlapHeight = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);

                if (overlapWidth <= TolerancePx || overlapHeight <= TolerancePx)
                {
                    continue;
                }

                var (upper, lower) = a.Top <= b.Top ? (a, b) : (b, a);
                overlaps.Add((upper.Top, new SlotOverlap(upper.Slot, lower.Slot, overlapWidth * overlapHeight)));
            }
        }

        return overlaps
            .OrderBy(entry => entry.UpperTop)
            .ThenBy(entry => entry.Overlap.UpperSlot, StringComparer.Ordinal)
            .Select(entry => entry.Overlap)
            .ToList();
    }

    /// <summary>Sums <see cref="SlotOverlap.IntersectionAreaPx2"/> across every reported overlap.</summary>
    public static double TotalIntersectionArea(IReadOnlyList<SlotOverlap> overlaps) =>
        overlaps.Sum(o => o.IntersectionAreaPx2);
}
