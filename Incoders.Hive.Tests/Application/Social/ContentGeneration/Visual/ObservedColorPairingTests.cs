using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// client-style-fidelity CSF-CG-03 / CSF-PA-04: observed colour pairings follow how often the client
/// uses each one, and a background that never appears in the client's pixels is dropped.
/// </summary>
public sealed class ObservedColorPairingTests
{
    private static readonly BrandSnapshot EmptyBrand = BrandSnapshot.FromBrandData(null, null, null);

    private static readonly ColorPairing BlackFlat =
        new("#0E0E0E", "#F2EDE2", 1, AccentTextHex: "#A6CE3C", SlideCount: 10, Kind: ColorPairingKinds.Flat);

    private static readonly ColorPairing CreamFlat =
        new("#ECE4D3", "#0E0E0E", 2, AccentTextHex: null, SlideCount: 1, Kind: ColorPairingKinds.Flat);

    private static readonly ColorPairing WhiteOnPhoto =
        new("#1A1A1A", "#FFFFFF", 1, AccentTextHex: "#A6CE3C", SlideCount: 6, Kind: ColorPairingKinds.Photo);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DominantPairing_FillsAShortCarousel(int slideIndex)
    {
        // CSF-CG-03-a: 10 slides vs 1 slide must not become a 50/50 alternation.
        var palette = Resolve([BlackFlat, CreamFlat], slideIndex, totalSlides: 3, SlideBackgroundKind.FlatColor);

        Assert.Equal("#0E0E0E", palette.Background);
        Assert.Equal("#F2EDE2", palette.Text);
        Assert.Equal("#A6CE3C", palette.Accent);
    }

    [Fact]
    public void BalancedPairings_ShareTheCarouselProportionally_DominantFirst()
    {
        var a = BlackFlat with { SlideCount = 5 };
        var b = CreamFlat with { SlideCount = 5 };

        var backgrounds = Enumerable.Range(1, 4)
            .Select(i => Resolve([a, b], i, totalSlides: 4, SlideBackgroundKind.FlatColor).Background)
            .ToArray();

        Assert.Equal(["#0E0E0E", "#0E0E0E", "#ECE4D3", "#ECE4D3"], backgrounds);
    }

    [Fact]
    public void PhotoSlide_TakesTheObservedTextOverPhotoPairing()
    {
        var palette = Resolve([BlackFlat, WhiteOnPhoto], slideIndex: 1, totalSlides: 3, SlideBackgroundKind.Photo);

        Assert.Equal("#FFFFFF", palette.Text);
        Assert.Equal("#A6CE3C", palette.Accent);
    }

    [Fact]
    public void FlatSlide_NeverTakesThePhotoPairing()
    {
        var palette = Resolve([WhiteOnPhoto, BlackFlat], slideIndex: 1, totalSlides: 1, SlideBackgroundKind.FlatColor);

        Assert.Equal("#0E0E0E", palette.Background);
    }

    [Fact]
    public void PhotoSlide_WithoutPhotoPairing_UsesTheDominantFlatColours()
    {
        var palette = Resolve([CreamFlat, BlackFlat], slideIndex: 2, totalSlides: 3, SlideBackgroundKind.Photo);

        Assert.Equal("#F2EDE2", palette.Text);
    }

    [Fact]
    public void DerivedPairings_KeepTheLegacyRotation()
    {
        // No SlideCount: pairings derived from palette roles, not observed. Unchanged behaviour.
        var palette = Resolve(
            [new ColorPairing("#AA0000", "#FFFFFF", 1), new ColorPairing("#00AA00", "#000000", 2)],
            slideIndex: 2, totalSlides: 3, SlideBackgroundKind.FlatColor);

        Assert.Equal("#00AA00", palette.Background);
    }

    // -- CSF-PA-04: pixel grounding ------------------------------------------------------------

    [Fact]
    public void Ground_DropsAFlatBackgroundTheClientNeverUses()
    {
        // The Demo Roasters case: a lime background derived from the accent colour.
        var lime = new ColorPairing("#A6CE3C", "#000000", 1);
        var coverage = new Dictionary<string, double> { ["#0E0E0E"] = 0.82, ["#A6CE3C"] = 0.04 };

        var grounded = ColorPairingEvidence.Ground([BlackFlat with { SlideCount = null, Kind = null }, lime], hex => coverage[hex]);

        Assert.Equal(["#0E0E0E"], grounded.Select(p => p.BackgroundHex));
    }

    [Fact]
    public void Ground_KeepsPhotoPairingsRegardlessOfCoverage()
    {
        var grounded = ColorPairingEvidence.Ground([WhiteOnPhoto], _ => 0);

        Assert.Single(grounded);
    }

    [Fact]
    public void Ground_KeepsABackgroundAtTheThreshold()
    {
        var grounded = ColorPairingEvidence.Ground([BlackFlat], _ => ColorPairingEvidence.MinimumCoverage);

        Assert.Single(grounded);
    }

    [Fact]
    public void Persistence_RoundTripsSlideCountAndKind()
    {
        var extraction = AestheticExtraction.Empty with { ColorPairings = [BlackFlat, WhiteOnPhoto] };

        var restored = AestheticProfilePersistence.Read(AestheticProfilePersistence.Serialize(extraction, []));

        Assert.Equal(10, restored.ColorPairings![0].SlideCount);
        Assert.Equal(ColorPairingKinds.Flat, restored.ColorPairings[0].Kind);
        Assert.Equal(ColorPairingKinds.Photo, restored.ColorPairings[1].Kind);
    }

    private static CompositionPalette Resolve(
        IReadOnlyList<ColorPairing> pairings, int slideIndex, int totalSlides, SlideBackgroundKind kind)
    {
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            ColorPairings: pairings);

        return CompositionDefaults.ResolvePalette(
            EmptyBrand, profile, slideIndex, new InferenceAccumulator(), totalSlides, kind);
    }
}
