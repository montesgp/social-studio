using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Carousel;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Composers;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// WU-3 TDD tests for <see cref="CarouselVisualComposer"/> palette-via-slide-index wiring.
///
/// Verifies:
/// - The composer calls <c>ResolvePalette</c> with the 1-based <c>slideIndex</c> of each slide
///   (rotation: <c>pairings[(slideIndex-1) % count]</c>).
/// - Slides in a multi-pairing set receive different palette backgrounds (rotation works).
/// - <see cref="CarouselVisualComposer"/> implements <see cref="IClientAestheticAwareComposer"/>.
///
/// Refs: CAC-CG-02; design §3.3, §0 OQ-2; tasks 3.3.
/// </summary>
public sealed class CarouselVisualComposerPaletteTests
{
    private static readonly BrandSnapshot EmptyBrand = BrandSnapshot.FromBrandData(null, null, null);

    // -----------------------------------------------------------------------
    // Interface contract
    // -----------------------------------------------------------------------

    [Fact]
    public void CarouselVisualComposer_ImplementsIClientAestheticAwareComposer()
    {
        var composer = new CarouselVisualComposer();
        Assert.IsAssignableFrom<IClientAestheticAwareComposer>(composer);
    }

    // -----------------------------------------------------------------------
    // Slide 1 and slide 2 in a 2-pairing set get different palette backgrounds
    // -----------------------------------------------------------------------

    /// <summary>
    /// GIVEN a profile with 2 pairings and a 2-slide carousel,
    /// WHEN ComposeMany is called via IClientAestheticAwareComposer,
    /// THEN slide 1 gets pairing[0] and slide 2 gets pairing[1] (different backgrounds).
    /// Rotation: (1-1)%2=0, (2-1)%2=1.
    /// Refs: CAC-CG-02.
    /// </summary>
    [Fact]
    public void ComposeMany_TwoPairings_TwoSlides_GetsDifferentPaletteBackgrounds()
    {
        var pairings = new List<ColorPairing>
        {
            new("#AAAAAA", "#111111", 1),
            new("#BBBBBB", "#222222", 2),
        };
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            ColorPairings: pairings);

        var composer = new CarouselVisualComposer();
        var aestheticAware = (IClientAestheticAwareComposer)composer;
        var payload = BuildCarouselPayload(2);
        var inferred = new InferenceAccumulator();

        var specs = aestheticAware.ComposeMany(payload, EmptyBrand, inferred, profile);

        Assert.Equal(2, specs.Count);
        Assert.Equal("#AAAAAA", specs[0].Palette.Background); // slideIndex=1 → (0)%2=0 → pairing[0]
        Assert.Equal("#BBBBBB", specs[1].Palette.Background); // slideIndex=2 → (1)%2=1 → pairing[1]
    }

    // -----------------------------------------------------------------------
    // 1-based slide index drives rotation (not 0-based array position)
    // -----------------------------------------------------------------------

    /// <summary>
    /// GIVEN a profile with 3 pairings and a 3-slide carousel,
    /// WHEN ComposeMany is called,
    /// THEN each slide gets its own pairing in order (1→0, 2→1, 3→2).
    /// Verifies: the index (slideIndex-1) is used, not the raw loop counter i.
    /// </summary>
    [Fact]
    public void ComposeMany_ThreePairings_ThreeSlides_EachGetsDistinctPairing()
    {
        var pairings = new List<ColorPairing>
        {
            new("#111111", "#FFFFFF", 1),
            new("#222222", "#000000", 2),
            new("#333333", "#FFFFFF", 3),
        };
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            ColorPairings: pairings);

        var composer = new CarouselVisualComposer();
        var aestheticAware = (IClientAestheticAwareComposer)composer;
        var payload = BuildCarouselPayload(3);
        var inferred = new InferenceAccumulator();

        var specs = aestheticAware.ComposeMany(payload, EmptyBrand, inferred, profile);

        Assert.Equal(3, specs.Count);
        Assert.Equal("#111111", specs[0].Palette.Background); // slideIndex=1 → (1-1)%3=0
        Assert.Equal("#222222", specs[1].Palette.Background); // slideIndex=2 → (2-1)%3=1
        Assert.Equal("#333333", specs[2].Palette.Background); // slideIndex=3 → (3-1)%3=2
    }

    /// <summary>
    /// GIVEN a profile with 2 pairings and a 4-slide carousel,
    /// WHEN ComposeMany is called,
    /// THEN slides 1 and 3 share pairing[0], slides 2 and 4 share pairing[1] (rotation wraps).
    /// </summary>
    [Fact]
    public void ComposeMany_TwoPairings_FourSlides_RotatesCorrectly()
    {
        var pairings = new List<ColorPairing>
        {
            new("#CC0000", "#FFFFFF", 1),
            new("#0000CC", "#FFFFFF", 2),
        };
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: null,
            ColorPairings: pairings);

        var composer = new CarouselVisualComposer();
        var aestheticAware = (IClientAestheticAwareComposer)composer;
        var payload = BuildCarouselPayload(4);
        var inferred = new InferenceAccumulator();

        var specs = aestheticAware.ComposeMany(payload, EmptyBrand, inferred, profile);

        Assert.Equal(4, specs.Count);
        Assert.Equal("#CC0000", specs[0].Palette.Background); // slideIndex=1 → (0)%2=0
        Assert.Equal("#0000CC", specs[1].Palette.Background); // slideIndex=2 → (1)%2=1
        Assert.Equal("#CC0000", specs[2].Palette.Background); // slideIndex=3 → (2)%2=0 → wraps
        Assert.Equal("#0000CC", specs[3].Palette.Background); // slideIndex=4 → (3)%2=1 → wraps
    }

    // -----------------------------------------------------------------------
    // Parity: null/empty profile → positional palette (cold-start)
    // -----------------------------------------------------------------------

    /// <summary>
    /// GIVEN profile=null,
    /// WHEN ComposeMany is called via IClientAestheticAwareComposer,
    /// THEN palette is the same as the legacy path (positional palette parity).
    /// CAC-BSP-04-a cold-start parity.
    /// </summary>
    [Fact]
    public void ComposeMany_NullProfile_FallsBackToPositionalPalette()
    {
        var composer = new CarouselVisualComposer();
        var aestheticAware = (IClientAestheticAwareComposer)composer;
        var payload = BuildCarouselPayload(2);
        var inferred = new InferenceAccumulator();

        var specs = aestheticAware.ComposeMany(payload, EmptyBrand, inferred, profile: null);

        Assert.Equal(2, specs.Count);
        // With null profile → positional palette → default Background = CompositionDefaults.Palette.Background
        var expectedBackground = CompositionDefaults.Palette.Background;
        Assert.Equal(expectedBackground, specs[0].Palette.Background);
        Assert.Equal(expectedBackground, specs[1].Palette.Background);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CarouselContent BuildCarouselPayload(int slideCount)
    {
        var slides = Enumerable.Range(1, slideCount)
            .Select(i => new CarouselSlide($"Slide {i} Title", $"Body {i}", null))
            .ToList();
        return new CarouselContent(
            CoverTitle: "Test Carousel",
            Slides: slides,
            CtaText: "Learn More",
            CtaLink: null);
    }
}
