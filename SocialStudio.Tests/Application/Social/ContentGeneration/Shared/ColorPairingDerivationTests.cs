using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Social.ClientBrandManuals;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// WU-1 task 1.9 [RED] — <see cref="ColorPairingDerivation.Derive"/> behaviour contract.
/// Refs: design §3.3 OQ-2; tasks 1.9/1.10; CAC-BSP-03.
/// </summary>
public sealed class ColorPairingDerivationTests
{
    // ── CAC-BSP-03-b: empty / null palette → empty list, no exception ──────────

    [Fact]
    public void EmptyPalette_ReturnsEmptyList_NoException()
    {
        var result = ColorPairingDerivation.Derive(
            [ColorRole.Primary, ColorRole.Secondary],
            []);

        Assert.Empty(result);
    }

    [Fact]
    public void EmptyBrandColors_ReturnsEmptyList_NoException()
    {
        var palette = new[]
        {
            new AestheticPaletteEntry("#1A2B3C", "Primary", 1),
        };

        var result = ColorPairingDerivation.Derive([], palette);

        Assert.Empty(result);
    }

    // ── CAC-BSP-03-a: joinable roles produce at least one pairing ──────────────

    [Fact]
    public void JoinableRoles_ProduceAtLeastOnePairing()
    {
        // Dark background (Primary role), white Neutral text
        var palette = new[]
        {
            new AestheticPaletteEntry("#0D0D0D", "Primary", 1),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 2),
        };

        var result = ColorPairingDerivation.Derive([ColorRole.Primary], palette);

        Assert.NotEmpty(result);
        Assert.Equal("#0D0D0D", result[0].BackgroundHex);
    }

    // ── Backgrounds ranked by (role ordinal, rank) ────────────────────────────

    [Fact]
    public void Backgrounds_OrderedByRoleOrdinalThenRank_PrimaryBeforeSecondary()
    {
        // Secondary rank 1, Primary rank 1 → Primary (ordinal 0) comes first
        var palette = new[]
        {
            new AestheticPaletteEntry("#222222", "Secondary", 1),
            new AestheticPaletteEntry("#111111", "Primary", 1),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 2),
        };

        var result = ColorPairingDerivation.Derive(
            [ColorRole.Primary, ColorRole.Secondary],
            palette);

        Assert.Equal(2, result.Count);
        Assert.Equal("#111111", result[0].BackgroundHex); // Primary comes first
        Assert.Equal("#222222", result[1].BackgroundHex); // Secondary second
    }

    [Fact]
    public void Backgrounds_SameRole_OrderedByRank_LowerRankFirst()
    {
        var palette = new[]
        {
            new AestheticPaletteEntry("#333333", "Primary", 2),
            new AestheticPaletteEntry("#111111", "Primary", 1),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 1),
        };

        var result = ColorPairingDerivation.Derive([ColorRole.Primary], palette);

        Assert.Equal(2, result.Count);
        Assert.Equal("#111111", result[0].BackgroundHex); // rank 1 first
        Assert.Equal("#333333", result[1].BackgroundHex); // rank 2 second
    }

    // ── Text chosen by highest WCAG contrast; Neutral-role candidates first ────

    [Fact]
    public void TextSelection_NeutralRoleWithSufficientContrast_ChosenOverLuminanceFallback()
    {
        // Dark background → Neutral #FFFFFF has 21:1 contrast (well above 4.5)
        var palette = new[]
        {
            new AestheticPaletteEntry("#000000", "Primary", 1),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 1),
        };

        var result = ColorPairingDerivation.Derive([ColorRole.Primary], palette);

        Assert.Single(result);
        Assert.Equal("#FFFFFF", result[0].TextHex);
    }

    [Fact]
    public void TextSelection_NoNeutral_DarkBackground_LuminanceFallbackChoosesWhite()
    {
        // Very dark background (#0D0D0D), no Neutral in palette → luminance picks white
        var palette = new[]
        {
            new AestheticPaletteEntry("#0D0D0D", "Primary", 1),
        };

        var result = ColorPairingDerivation.Derive([ColorRole.Primary], palette);

        Assert.Single(result);
        Assert.Equal("#FFFFFF", result[0].TextHex);
    }

    [Fact]
    public void TextSelection_NoNeutral_LightBackground_LuminanceFallbackChoosesBlack()
    {
        // Very light background (#F0F0F0), no Neutral → luminance picks black
        var palette = new[]
        {
            new AestheticPaletteEntry("#F0F0F0", "Primary", 1),
        };

        var result = ColorPairingDerivation.Derive([ColorRole.Primary], palette);

        Assert.Single(result);
        Assert.Equal("#000000", result[0].TextHex);
    }

    [Fact]
    public void TextSelection_NeutralWithInsufficientContrast_FallsBackToLuminance()
    {
        // Light background (#F0F0F0), Neutral is white (#FFFFFF) — contrast ≈ 1.05:1, fails WCAG
        // Luminance fallback should choose black (#000000) which has high contrast
        var palette = new[]
        {
            new AestheticPaletteEntry("#F0F0F0", "Primary", 1),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 2), // almost same as background — fails 4.5:1
        };

        var result = ColorPairingDerivation.Derive([ColorRole.Primary], palette);

        Assert.Single(result);
        Assert.Equal("#000000", result[0].TextHex);
    }

    // ── Palette entries with no matching brand role → not used ────────────────

    [Fact]
    public void PaletteEntriesWithNonMatchingRole_AreExcluded()
    {
        // Brand only allows Accent; palette has Primary + Accent + Neutral
        var palette = new[]
        {
            new AestheticPaletteEntry("#111111", "Primary", 1),
            new AestheticPaletteEntry("#9B59B6", "Accent", 2),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 3),
        };

        var result = ColorPairingDerivation.Derive([ColorRole.Accent], palette);

        Assert.Single(result);
        Assert.Equal("#9B59B6", result[0].BackgroundHex); // only Accent used as background
    }

    // ── Palette entries with unrecognised role string → ignored ───────────────

    [Fact]
    public void PaletteEntries_WithUnrecognisedRoleString_AreIgnoredAsBackgrounds()
    {
        var palette = new[]
        {
            new AestheticPaletteEntry("#ABCDEF", "unknown-role", 1),
            new AestheticPaletteEntry("#123456", "Primary", 2),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 3),
        };

        var result = ColorPairingDerivation.Derive([ColorRole.Primary], palette);

        Assert.Single(result);
        Assert.Equal("#123456", result[0].BackgroundHex);
    }

    // ── Neutral role is NEVER used as a background even if in brand colors ─────

    [Fact]
    public void NeutralRole_EvenIfInBrandColors_IsNotUsedAsBackground()
    {
        var palette = new[]
        {
            new AestheticPaletteEntry("#AAAAAA", "Neutral", 1),
        };

        // Brand explicitly includes Neutral (unusual, but must not produce a background)
        var result = ColorPairingDerivation.Derive([ColorRole.Neutral], palette);

        Assert.Empty(result);
    }

    // ── Rank on returned ColorPairing reflects source palette entry rank ───────

    [Fact]
    public void ColorPairing_Rank_MatchesSourcePaletteEntryRank()
    {
        var palette = new[]
        {
            new AestheticPaletteEntry("#1A1A2E", "Primary", 7),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 1),
        };

        var result = ColorPairingDerivation.Derive([ColorRole.Primary], palette);

        Assert.Single(result);
        Assert.Equal(7, result[0].Rank);
    }
}
