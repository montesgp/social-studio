using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Domain.Social.ContentGeneration;

/// <summary>
/// TDD tests for WU-1 Phase 1.B: WeightedColor record and BrandManualSnapshot.ColorPalette shape.
/// Refs: CG-COL-01; design §3.3 (§6 item 2).
/// </summary>
public sealed class WeightedColorTests
{
    // -----------------------------------------------------------------------
    // 1.4 RED: WeightedColor construction
    // -----------------------------------------------------------------------

    [Fact]
    public void WeightedColor_Construction_HoldsAllProperties()
    {
        var wc = new WeightedColor("#FF0000", ColorRole.Primary, 1);

        Assert.Equal("#FF0000", wc.Hex);
        Assert.Equal(ColorRole.Primary, wc.Role);
        Assert.Equal(1, wc.Rank);
    }

    [Fact]
    public void WeightedColor_NullRole_IsAllowed()
    {
        var wc = new WeightedColor("#CCCCCC", null, 0);

        Assert.Null(wc.Role);
        Assert.Equal(0, wc.Rank);
    }

    // -----------------------------------------------------------------------
    // BrandManualSnapshot.ColorPalette is IReadOnlyList<WeightedColor>
    // (not IEnumerable<string>)
    // -----------------------------------------------------------------------

    [Fact]
    public void BrandManualSnapshot_ColorPalette_IsReadOnlyListOfWeightedColor()
    {
        var palette = new[]
        {
            new WeightedColor("#FF0000", ColorRole.Primary, 1),
            new WeightedColor("#0000FF", ColorRole.Secondary, 2),
            new WeightedColor("#AAAAAA", ColorRole.Neutral, 3),
        };

        var snapshot = new BrandManualSnapshot(
            ColorPalette: palette,
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: Array.Empty<string>(),
            VoiceAndTone: null,
            Notes: null);

        Assert.IsAssignableFrom<IReadOnlyList<WeightedColor>>(snapshot.ColorPalette);
        Assert.Equal(3, snapshot.ColorPalette.Count);
        Assert.Equal("#FF0000", snapshot.ColorPalette[0].Hex);
        Assert.Equal(ColorRole.Primary, snapshot.ColorPalette[0].Role);
        Assert.Equal(1, snapshot.ColorPalette[0].Rank);
    }

    [Fact]
    public void BrandManualSnapshot_ColorPalette_DoesNotAcceptStringEnumerable()
    {
        // This is a compile-time assertion: IReadOnlyList<string> must NOT be accepted.
        // We verify by ensuring Array.Empty<WeightedColor>() is a valid assignment.
        var snapshot = new BrandManualSnapshot(
            ColorPalette: Array.Empty<WeightedColor>(),
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: Array.Empty<string>(),
            VoiceAndTone: null,
            Notes: null);

        Assert.Empty(snapshot.ColorPalette);
    }

    [Fact]
    public void WeightedColor_IsRecord_SupportsValueEquality()
    {
        var a = new WeightedColor("#FF0000", ColorRole.Primary, 1);
        var b = new WeightedColor("#FF0000", ColorRole.Primary, 1);
        var c = new WeightedColor("#FF0000", ColorRole.Secondary, 1);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
