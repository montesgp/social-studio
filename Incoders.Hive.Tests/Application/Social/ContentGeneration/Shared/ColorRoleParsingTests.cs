using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ClientBrandManuals;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// Role parsing against what the multimodal extractor ACTUALLY returns.
///
/// <para>
/// Role used to be matched by exact string equality against "primary"/"secondary"/"accent"/"neutral".
/// The model does not answer that way. Analysis a4e1179a (2026-08-22, @atlas.editorial) returned
/// descriptive roles like <c>"primary-accent-and-background (chartreuse/yellow-green)"</c> and
/// <c>"body text on light backgrounds"</c>, so every entry failed to parse, no background candidate
/// survived, and <c>colorPairings</c> persisted as null — silently disabling the whole rotating
/// palette AND the three-colour accent that depends on it.
/// </para>
///
/// <para>
/// The palette strings below are copied verbatim from that stored profile.
/// </para>
/// </summary>
public sealed class ColorRoleParsingTests
{
    /// <summary>The real palette from analysis a4e1179a, roles unedited.</summary>
    private static AestheticPaletteEntry[] AtlasPalette() =>
    [
        new("#E1F03D", "primary-accent-and-background (chartreuse/yellow-green)", 1),
        new("#EE3D29", "secondary-background (red-orange)", 1),
        new("#83B9DE", "background (sky blue)", 2),
        new("#B49CE6", "background (lavender purple)", 3),
        new("#12492E", "background (deep green)", 4),
        new("#FFFFFF", "text on photos/dark backgrounds", 5),
        new("#111111", "body text on light backgrounds", 6),
    ];

    [Fact]
    public void RealExtractorRoles_ProduceColorPairings_NotAnEmptyList()
    {
        var pairings = ColorPairingDerivation.Derive(
            [ColorRole.Primary, ColorRole.Secondary, ColorRole.Accent],
            AtlasPalette());

        // Five backgrounds, two text colours — every background must get a pairing.
        Assert.Equal(5, pairings.Count);
        Assert.All(pairings, p => Assert.False(string.IsNullOrWhiteSpace(p.TextHex)));
    }

    [Fact]
    public void ATextRoleIsNeverUsedAsABackground()
    {
        var pairings = ColorPairingDerivation.Derive(
            [ColorRole.Primary, ColorRole.Secondary, ColorRole.Accent],
            AtlasPalette());

        // "body text on light backgrounds" contains the word "background"; a naive substring check
        // that tested background before text would promote the body colour to a slide background.
        Assert.DoesNotContain(pairings, p => p.BackgroundHex is "#FFFFFF" or "#111111");
    }

    [Fact]
    public void TheDarkAndLightTextColoursAreAvailableAsBodyText()
    {
        var pairings = ColorPairingDerivation.Derive(
            [ColorRole.Primary, ColorRole.Secondary, ColorRole.Accent],
            AtlasPalette());

        // Chartreuse is light, so it takes the dark body colour; deep green is dark and takes white.
        var chartreuse = pairings.Single(p => p.BackgroundHex == "#E1F03D");
        var deepGreen = pairings.Single(p => p.BackgroundHex == "#12492E");

        Assert.Equal("#111111", chartreuse.TextHex);
        Assert.Equal("#FFFFFF", deepGreen.TextHex);
    }

    [Fact]
    public void TheThreeColourAccentFiresOnRealData()
    {
        var pairings = ColorPairingDerivation.Derive(
            [ColorRole.Primary, ColorRole.Secondary, ColorRole.Accent],
            AtlasPalette());

        // The whole point of the accent work: at least one slide gets a legible third colour.
        Assert.Contains(pairings, p => p.AccentTextHex is not null);
    }

    [Fact]
    public void PrimaryOutranksAccentWhenARoleClaimsBoth()
    {
        // "primary-accent-and-background" names two roles. Precedence must be deterministic and
        // follow the brand hierarchy, so this sorts as a Primary background, ahead of the plain ones.
        var pairings = ColorPairingDerivation.Derive(
            [ColorRole.Primary, ColorRole.Secondary, ColorRole.Accent],
            AtlasPalette());

        Assert.Equal("#E1F03D", pairings[0].BackgroundHex);
        Assert.Equal("#EE3D29", pairings[1].BackgroundHex);
    }

    [Theory]
    [InlineData("primary", ColorRole.Primary)]
    [InlineData("  Secondary  ", ColorRole.Secondary)]
    [InlineData("ACCENT", ColorRole.Accent)]
    [InlineData("neutral", ColorRole.Neutral)]
    [InlineData("secondary-background (red-orange)", ColorRole.Secondary)]
    [InlineData("background (sky blue)", ColorRole.Accent)]
    [InlineData("text on photos/dark backgrounds", ColorRole.Neutral)]
    [InlineData("body text on light backgrounds", ColorRole.Neutral)]
    public void RoleTokensParseRegardlessOfSurroundingProse(string role, ColorRole expected)
    {
        // A single-entry palette isolates the parse: if the role resolves to a background role the
        // entry becomes a pairing; if it resolves to Neutral it cannot.
        var palette = new[] { new AestheticPaletteEntry("#123456", role, 1) };

        var asBackground = ColorPairingDerivation.Derive(
            [ColorRole.Primary, ColorRole.Secondary, ColorRole.Accent], palette);

        var onlyThisRole = ColorPairingDerivation.Derive([expected], palette);

        if (expected == ColorRole.Neutral)
        {
            Assert.Empty(asBackground);
        }
        else
        {
            Assert.Single(asBackground);
            Assert.Single(onlyThisRole);
        }
    }

    [Fact]
    public void AnUnrecognisableRoleIsStillIgnored()
    {
        // Garbage in must not become a background — the previous strictness protected against that
        // and the tolerant parser has to keep protecting against it.
        var palette = new[] { new AestheticPaletteEntry("#123456", "sparkly vibes", 1) };

        Assert.Empty(ColorPairingDerivation.Derive(
            [ColorRole.Primary, ColorRole.Secondary, ColorRole.Accent], palette));
    }
}
