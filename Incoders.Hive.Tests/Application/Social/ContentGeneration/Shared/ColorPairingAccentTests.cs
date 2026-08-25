using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ClientBrandManuals;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Shared;

/// <summary>
/// Three-colour slide combinations.
///
/// <para>
/// A pairing used to be exactly two colours — one background, one text. Real client feeds routinely
/// run a third: a fixed background with body copy in one colour and emphasised copy in another
/// (e.g. red background, white body, yellow highlight). <see cref="ColorPairing.AccentTextHex"/>
/// carries that third colour, and stays null when the palette offers no legible candidate, which
/// keeps two-colour clients byte-identical to the previous behaviour.
/// </para>
/// </summary>
public sealed class ColorPairingAccentTests
{
    [Fact]
    public void Derive_PicksAnAccentDistinctFromBothBackgroundAndBodyText()
    {
        // Deep red background, white neutral body text, saturated yellow available as emphasis.
        var palette = new[]
        {
            new AestheticPaletteEntry("#8A0303", "Primary", 1),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 1),
            new AestheticPaletteEntry("#F2D024", "Accent", 2),
        };

        var pairing = Assert.Single(
            ColorPairingDerivation.Derive([ColorRole.Primary], palette));

        Assert.Equal("#8A0303", pairing.BackgroundHex);
        Assert.Equal("#FFFFFF", pairing.TextHex);
        Assert.Equal("#F2D024", pairing.AccentTextHex);
    }

    [Fact]
    public void Derive_LeavesAccentNullWhenNoThirdColourIsLegibleOnTheBackground()
    {
        // Only a background and its body text; nothing else to emphasise with.
        var palette = new[]
        {
            new AestheticPaletteEntry("#0D0D0D", "Primary", 1),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 1),
        };

        var pairing = Assert.Single(
            ColorPairingDerivation.Derive([ColorRole.Primary], palette));

        Assert.Null(pairing.AccentTextHex);
    }

    [Fact]
    public void Derive_NeverReusesTheBodyTextColourAsTheAccent()
    {
        // The only non-background candidate is already spoken for as body text.
        var palette = new[]
        {
            new AestheticPaletteEntry("#101820", "Primary", 1),
            new AestheticPaletteEntry("#F5F5F5", "Neutral", 1),
            new AestheticPaletteEntry("#F5F5F5", "Accent", 2),
        };

        var pairing = Assert.Single(
            ColorPairingDerivation.Derive([ColorRole.Primary], palette));

        Assert.NotEqual(pairing.TextHex, pairing.AccentTextHex);
        Assert.Null(pairing.AccentTextHex);
    }

    [Fact]
    public void Derive_RejectsAnAccentThatFailsContrastAgainstTheBackground()
    {
        // #1A1A1A on #0D0D0D is unreadable — it must not be promoted to an accent.
        var palette = new[]
        {
            new AestheticPaletteEntry("#0D0D0D", "Primary", 1),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 1),
            new AestheticPaletteEntry("#1A1A1A", "Accent", 2),
        };

        var pairing = Assert.Single(
            ColorPairingDerivation.Derive([ColorRole.Primary], palette));

        Assert.Null(pairing.AccentTextHex);
    }

    [Fact]
    public void Derive_KeepsEachBackgroundsAccentIndependent()
    {
        // Two backgrounds, each getting the best legible accent for ITS own luminance.
        var palette = new[]
        {
            new AestheticPaletteEntry("#0D0D0D", "Primary", 1),
            new AestheticPaletteEntry("#F7F3EA", "Secondary", 1),
            new AestheticPaletteEntry("#FFFFFF", "Neutral", 1),
            new AestheticPaletteEntry("#D4F24A", "Accent", 2),
        };

        var pairings = ColorPairingDerivation.Derive(
            [ColorRole.Primary, ColorRole.Secondary], palette);

        Assert.Equal(2, pairings.Count);

        var dark = pairings.Single(p => p.BackgroundHex == "#0D0D0D");
        var light = pairings.Single(p => p.BackgroundHex == "#F7F3EA");

        // The lime is the brand's Accent role and reads on near-black, so it wins there — even though
        // the cream entry has HIGHER raw contrast, a near-white "highlight" on a dark slide would be
        // indistinguishable from the white body copy.
        Assert.Equal("#D4F24A", dark.AccentTextHex);

        // On cream the lime fails contrast, so emphasis falls to the next legible chromatic entry.
        Assert.Equal("#0D0D0D", light.AccentTextHex);
        Assert.NotEqual(dark.AccentTextHex, light.AccentTextHex);
    }

    [Fact]
    public void ProfileRoundTrip_PreservesTheAccentAcrossPersistence()
    {
        var extraction = AestheticExtraction.Empty with
        {
            ColorPairings =
            [
                new ColorPairing("#8A0303", "#FFFFFF", 1, "#F2D024"),
                new ColorPairing("#101820", "#F5F5F5", 2),
            ],
        };

        var json = AestheticProfilePersistence.Serialize(extraction, []);
        var restored = AestheticProfilePersistence.Read(json);

        Assert.NotNull(restored.ColorPairings);
        Assert.Equal("#F2D024", restored.ColorPairings![0].AccentTextHex);
        // A two-colour pairing round-trips as a two-colour pairing.
        Assert.Null(restored.ColorPairings[1].AccentTextHex);
    }

    [Fact]
    public void StoredBlobsWrittenBeforeAccentsExisted_ReadBackAsTwoColourPairings()
    {
        // A pre-accent blob has no accentTextHex key at all; it must not fail or invent one.
        const string legacyJson =
            """{"palette":[],"styleDescriptors":[],"motifs":[],"captionVoice":null,"referenceImages":[],"colorPairings":[{"backgroundHex":"#8A0303","textHex":"#FFFFFF","rank":1}]}""";

        var restored = AestheticProfilePersistence.Read(legacyJson);

        var pairing = Assert.Single(restored.ColorPairings!);
        Assert.Equal("#8A0303", pairing.BackgroundHex);
        Assert.Null(pairing.AccentTextHex);
    }
}
