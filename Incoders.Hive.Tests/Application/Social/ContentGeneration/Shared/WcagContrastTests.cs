using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-3 task 3.1 [RED] — <see cref="WcagContrast"/> shared luminance/contrast maths, extracted
/// (behaviour-identical) from <see cref="ColorPairingDerivation"/>'s former privates.
/// Refs: design D4; tasks 3.1; CAGH-CG-03.
/// </summary>
public sealed class WcagContrastTests
{
    // ── RelativeLuminance ───────────────────────────────────────────────────

    [Fact]
    public void RelativeLuminance_Black_IsZero()
    {
        Assert.Equal(0.0, WcagContrast.RelativeLuminance("#000000"), 4);
    }

    [Fact]
    public void RelativeLuminance_White_IsOne()
    {
        Assert.Equal(1.0, WcagContrast.RelativeLuminance("#FFFFFF"), 4);
    }

    [Fact]
    public void RelativeLuminance_ShorthandHex_ExpandsCorrectly()
    {
        Assert.Equal(WcagContrast.RelativeLuminance("#FFFFFF"), WcagContrast.RelativeLuminance("#FFF"), 4);
    }

    [Fact]
    public void RelativeLuminance_UnrecognisedInput_ReturnsZero_NoException()
    {
        Assert.Equal(0.0, WcagContrast.RelativeLuminance("not-a-color"));
    }

    // ── ContrastRatio(double, double) ───────────────────────────────────────

    [Fact]
    public void ContrastRatio_BlackVsWhite_IsMaximum21()
    {
        Assert.Equal(21.0, WcagContrast.ContrastRatio(0.0, 1.0), 2);
    }

    [Fact]
    public void ContrastRatio_SameLuminance_IsOne()
    {
        Assert.Equal(1.0, WcagContrast.ContrastRatio(0.5, 0.5), 2);
    }

    // ── ContrastRatio(string, string) hex overload ──────────────────────────

    [Fact]
    public void ContrastRatio_HexOverload_WhiteVsBlack_IsMaximum21()
    {
        Assert.Equal(21.0, WcagContrast.ContrastRatio("#FFFFFF", "#000000"), 2);
    }

    [Fact]
    public void ContrastRatio_HexOverload_PaleColorAgainstBlack_ClearsGuard()
    {
        // #E0E0E0 is a light neutral — clears 4.5:1 against black.
        Assert.True(WcagContrast.ContrastRatio("#E0E0E0", "#000000") >= 4.5);
    }

    [Fact]
    public void ContrastRatio_HexOverload_DarkColorAgainstBlack_FailsGuard()
    {
        // A near-black colour fails 4.5:1 against black.
        Assert.True(WcagContrast.ContrastRatio("#111111", "#000000") < 4.5);
    }
}
