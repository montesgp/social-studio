using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-1 task 1.3 [RED] — <see cref="ColorPairing"/> record contract.
/// Refs: design §3.1 OQ-2; tasks 1.3/1.4; CAC-BSP-03.
/// </summary>
public sealed class ColorPairingTests
{
    [Fact]
    public void ColorPairing_ConstructsWithAllThreeParams()
    {
        var pairing = new ColorPairing("#1A2B3C", "#FFFFFF", 1);

        Assert.Equal("#1A2B3C", pairing.BackgroundHex);
        Assert.Equal("#FFFFFF", pairing.TextHex);
        Assert.Equal(1, pairing.Rank);
    }

    [Fact]
    public void ColorPairing_IsImmutable_WithExpressionProducesNewInstance()
    {
        var original = new ColorPairing("#1A2B3C", "#FFFFFF", 1);
        var modified = original with { TextHex = "#000000" };

        Assert.Equal("#1A2B3C", original.BackgroundHex); // original unchanged
        Assert.Equal("#FFFFFF", original.TextHex);
        Assert.Equal("#000000", modified.TextHex);
        Assert.Equal("#1A2B3C", modified.BackgroundHex);
        Assert.Equal(1, modified.Rank);
    }

    [Fact]
    public void ColorPairing_WithExpression_ChangeRank_CompilesAndYieldsNewRank()
    {
        var original = new ColorPairing("#AABBCC", "#111111", 3);
        var updated = original with { Rank = 7 };

        Assert.Equal(7, updated.Rank);
        Assert.Equal(3, original.Rank); // original unchanged
    }

    [Fact]
    public void ColorPairing_RecordEquality_SameValues_AreEqual()
    {
        var a = new ColorPairing("#AABBCC", "#FFFFFF", 1);
        var b = new ColorPairing("#AABBCC", "#FFFFFF", 1);

        Assert.Equal(a, b);
    }

    [Fact]
    public void ColorPairing_RecordEquality_DifferentRank_NotEqual()
    {
        var a = new ColorPairing("#AABBCC", "#FFFFFF", 1);
        var b = new ColorPairing("#AABBCC", "#FFFFFF", 2);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ColorPairing_IsInApplicationNamespace()
    {
        Assert.StartsWith("Incoders.Hive.Application", typeof(ColorPairing).Namespace);
    }
}
