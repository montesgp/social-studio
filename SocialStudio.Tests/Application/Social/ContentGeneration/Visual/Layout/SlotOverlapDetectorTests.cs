using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Layout;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Visual.Layout;

/// <summary>
/// WU-2 TDD tests for <see cref="SlotOverlapDetector"/> — pure pairwise cross-slot overlap
/// detection over measured Chromium geometry. Spec: CLIF-CG-01-a, CLIF-CG-01-b.
/// Tasks: 2.1, 2.2, 2.3.
/// </summary>
public sealed class SlotOverlapDetectorTests
{
    [Fact]
    public void Detect_RealDefectFixture_HeadlineOverlapsSubtitle_IsFlagged()
    {
        // GIVEN a headline slot rendered at font-size:149px, top:255px, wrapping to 2-3
        // lines (measured height 447px) and a subtitle slot at top:575px — the real defect
        // fixture from the production carousel job.
        var headline = new SlotBox("headline", Left: 0, Top: 255, Width: 1080, Height: 447);
        var subtitle = new SlotBox("subtitle", Left: 0, Top: 575, Width: 1080, Height: 200);

        var overlaps = SlotOverlapDetector.Detect(new[] { headline, subtitle });

        Assert.Single(overlaps);
        var overlap = overlaps[0];
        Assert.Equal("headline", overlap.UpperSlot);
        Assert.Equal("subtitle", overlap.LowerSlot);
        // headline bottom = 255 + 447 = 702; subtitle top = 575 => vertical intersection = 127px.
        // full-width slots => horizontal intersection = 1080px.
        Assert.Equal(1080d * 127d, overlap.IntersectionAreaPx2, precision: 3);
    }

    [Fact]
    public void Detect_SideBySideSlots_OverlappingVerticalBandOnly_IsNotFlagged()
    {
        // GIVEN two slots whose vertical bands overlap but whose horizontal bands do not
        // (side-by-side layout) — a vertical-band-only test would false-positive here.
        var left = new SlotBox("left-column", Left: 0, Top: 100, Width: 500, Height: 200);
        var right = new SlotBox("right-column", Left: 600, Top: 150, Width: 500, Height: 200);

        var overlaps = SlotOverlapDetector.Detect(new[] { left, right });

        Assert.Empty(overlaps);
    }

    [Fact]
    public void Detect_MultipleOverlappingPairs_AreOrderedDeterministicallyAndAreaSumsCorrectly()
    {
        // GIVEN three stacked slots fed in a shuffled order, where A overlaps B and B
        // overlaps C, but A does not overlap C.
        var a = new SlotBox("a", Left: 0, Top: 0, Width: 100, Height: 100);   // bottom = 100
        var b = new SlotBox("b", Left: 0, Top: 50, Width: 100, Height: 100);  // bottom = 150
        var c = new SlotBox("c", Left: 0, Top: 120, Width: 100, Height: 100); // bottom = 220

        var overlaps = SlotOverlapDetector.Detect(new[] { c, a, b });

        Assert.Equal(2, overlaps.Count);
        // Ordered by (Top, Slot) — a's pair (Top 0) before b's pair (Top 50).
        Assert.Equal("a", overlaps[0].UpperSlot);
        Assert.Equal("b", overlaps[0].LowerSlot);
        Assert.Equal("b", overlaps[1].UpperSlot);
        Assert.Equal("c", overlaps[1].LowerSlot);

        // (a,b) intersection: height = 100 - 50 = 50, width = 100 => area = 5000.
        Assert.Equal(5000d, overlaps[0].IntersectionAreaPx2, precision: 3);
        // (b,c) intersection: height = 150 - 120 = 30, width = 100 => area = 3000.
        Assert.Equal(3000d, overlaps[1].IntersectionAreaPx2, precision: 3);

        Assert.Equal(8000d, SlotOverlapDetector.TotalIntersectionArea(overlaps), precision: 3);
    }

    [Fact]
    public void Detect_TouchingSlotsWithinTolerance_AreNotFlagged()
    {
        // GIVEN two slots whose vertical bands touch by exactly TolerancePx (1.0) — the
        // design requires BOTH axes to intersect by MORE than TolerancePx to count.
        var upper = new SlotBox("upper", Left: 0, Top: 0, Width: 100, Height: 100); // bottom = 100
        var lower = new SlotBox("lower", Left: 0, Top: 99, Width: 100, Height: 100); // overlap = 1px

        var overlaps = SlotOverlapDetector.Detect(new[] { upper, lower });

        Assert.Empty(overlaps);
    }
}
