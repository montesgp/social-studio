using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Composers;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// client-style-fidelity CSF-CG-06: the composer carries each slide's highlight and the colour the
/// client was observed using for it — never a derived accent.
/// </summary>
public sealed class CarouselVisualComposerHighlightTests
{
    private static readonly BrandSnapshot EmptyBrand = BrandSnapshot.FromBrandData(null, null, null);

    private static readonly CarouselContent Payload = new(
        CoverTitle: "¿Sabés qué toman tus clientes?",
        Slides:
        [
            new CarouselSlide("Cover", "Body", null),
            new CarouselSlide("El tostado es precisión", "Body", null, TitleHighlight: "precisión"),
        ],
        CtaText: string.Empty,
        CtaLink: null,
        CoverTitleHighlight: "tus clientes");

    [Fact]
    public void ObservedHighlightColour_TravelsWithEachSlidesHighlight()
    {
        var specs = Compose(new ColorPairing("#0E0E0E", "#FFFFFF", 1, "#A6CE3C", SlideCount: 10, Kind: ColorPairingKinds.Flat));

        Assert.Equal("tus clientes", specs[0].TextHighlights!["title"]);
        Assert.Equal("precisión", specs[1].TextHighlights!["title"]);
        Assert.All(specs, s => Assert.Equal("#A6CE3C", s.HighlightColor));
    }

    [Fact]
    public void DerivedPairing_CarriesNoHighlightColour()
    {
        var specs = Compose(new ColorPairing("#0E0E0E", "#FFFFFF", 1, "#A6CE3C"));

        Assert.All(specs, s => Assert.Null(s.HighlightColor));
    }

    private static IReadOnlyList<VisualCompositionSpec> Compose(ColorPairing pairing)
    {
        var profile = new ClientAestheticProfile([], null, null, null, ColorPairings: [pairing]);
        return new CarouselVisualComposer().ComposeMany(Payload, EmptyBrand, new InferenceAccumulator(), profile);
    }
}
