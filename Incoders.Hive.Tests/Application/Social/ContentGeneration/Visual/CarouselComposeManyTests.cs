using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// T3.10 — TDD tests for carousel ComposeMany multi-slide fan-out.
/// Verifies that CarouselVisualComposer (via VisualCompositionService.ComposeManyAsync)
/// returns one VisualCompositionSpec per slide.
/// </summary>
public sealed class CarouselComposeManyTests
{
    private static readonly BrandSnapshot EmptyBrand = BrandSnapshot.FromBrandData(null, null, null);

    [Fact]
    public async Task ComposeManyAsync_ThreeSlides_ReturnsThreeSpecs()
    {
        var service = new VisualCompositionService();
        var payload = BuildCarouselContent(3);

        var specs = await service.ComposeManyAsync("carousel", payload, EmptyBrand, CancellationToken.None);

        Assert.Equal(3, specs.Count);
    }

    [Fact]
    public async Task ComposeManyAsync_FiveSlides_ReturnsFiveSpecs()
    {
        var service = new VisualCompositionService();
        var payload = BuildCarouselContent(5);

        var specs = await service.ComposeManyAsync("carousel", payload, EmptyBrand, CancellationToken.None);

        Assert.Equal(5, specs.Count);
    }

    [Fact]
    public async Task ComposeManyAsync_AllSpecs_FormatKeyIsCarousel()
    {
        var service = new VisualCompositionService();
        var payload = BuildCarouselContent(3);

        var specs = await service.ComposeManyAsync("carousel", payload, EmptyBrand, CancellationToken.None);

        Assert.All(specs, spec => Assert.Equal("carousel", spec.FormatKey));
    }

    [Fact]
    public async Task ComposeManyAsync_FlyerFormat_ReturnsSingleSpec()
    {
        // Single-asset formats return a 1-element list via the fallback path.
        var service = new VisualCompositionService();

        var specs = await service.ComposeManyAsync("story", new object(), EmptyBrand, CancellationToken.None);

        Assert.Single(specs);
    }

    [Fact]
    public async Task ComposeManyAsync_ZeroSlideCarousel_ReturnsEmptyList()
    {
        var service = new VisualCompositionService();
        var payload = BuildCarouselContent(0);

        var specs = await service.ComposeManyAsync("carousel", payload, EmptyBrand, CancellationToken.None);

        Assert.Empty(specs);
    }

    [Fact]
    public async Task ComposeManyAsync_TextFormats_ReturnSingleSpecEach()
    {
        var service = new VisualCompositionService();
        string[] textFormats = ["reel-script", "caption", "hook", "content-idea", "cta-variation"];

        foreach (var formatKey in textFormats)
        {
            var specs = await service.ComposeManyAsync(formatKey, new object(), EmptyBrand, CancellationToken.None);
            Assert.Single(specs);
        }
    }

    private static CarouselContent BuildCarouselContent(int slideCount) => new(
        CoverTitle: "Test Cover",
        Slides: Enumerable.Range(1, slideCount)
            .Select(i => new CarouselSlide($"Slide {i} Title", $"Slide {i} body text.", null))
            .ToList(),
        CtaText: "Learn More",
        CtaLink: null);
}
