using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Carousel;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Composers;
using SocialStudio.Domain.Social.ContentFormats;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Visual;

/// <summary>
/// S2-T5 / SCENARIO-19 / S5: SurfaceScopedCarouselVisualComposer decorator tests.
/// Verifies the decorator overrides FormatKey and threads it into composed specs (S5 plumbing guard).
/// </summary>
public sealed class SurfaceScopedCarouselVisualComposerTests
{
    private static readonly FormatDefinition StoryCarouselDef = FormatCatalog.TryGet("story-carousel")!;
    private static readonly FormatDefinition FeedCarouselDef = FormatCatalog.TryGet("feed-carousel")!;

    [Fact]
    public void FormatKey_StoryCarouselDecorator_ReturnsStoryCarousel()
    {
        var decorator = new SurfaceScopedCarouselVisualComposer(StoryCarouselDef, new CarouselVisualComposer());

        Assert.Equal("story-carousel", decorator.FormatKey);
    }

    [Fact]
    public void FormatKey_FeedCarouselDecorator_ReturnsFeedCarousel()
    {
        var decorator = new SurfaceScopedCarouselVisualComposer(FeedCarouselDef, new CarouselVisualComposer());

        Assert.Equal("feed-carousel", decorator.FormatKey);
    }

    [Fact]
    public void Compose_StoryCarousel_EmittedSpecHasSurfaceScopedFormatKey()
    {
        // S5 plumbing guard: spec.FormatKey must be "story-carousel", not "carousel"
        var decorator = new SurfaceScopedCarouselVisualComposer(StoryCarouselDef, new CarouselVisualComposer());
        var payload = MakeCarouselPayload();
        var brand = BrandSnapshot.Empty;
        var inferred = new InferenceAccumulator();

        var spec = decorator.Compose(payload, brand, inferred);

        Assert.Equal("story-carousel", spec.FormatKey);
    }

    [Fact]
    public void Compose_FeedCarousel_EmittedSpecHasSurfaceScopedFormatKey()
    {
        var decorator = new SurfaceScopedCarouselVisualComposer(FeedCarouselDef, new CarouselVisualComposer());
        var payload = MakeCarouselPayload();
        var brand = BrandSnapshot.Empty;
        var inferred = new InferenceAccumulator();

        var spec = decorator.Compose(payload, brand, inferred);

        Assert.Equal("feed-carousel", spec.FormatKey);
    }

    [Fact]
    public void ComposeMany_StoryCarousel_AllSpecsHaveSurfaceScopedFormatKey()
    {
        // S5 per-surface-template guard: every slide spec must carry the scoped key
        var decorator = new SurfaceScopedCarouselVisualComposer(StoryCarouselDef, new CarouselVisualComposer());
        var payload = MakeCarouselPayload(slideCount: 3);
        var brand = BrandSnapshot.Empty;
        var inferred = new InferenceAccumulator();

        var specs = decorator.ComposeMany(payload, brand, inferred);

        Assert.Equal(3, specs.Count);
        Assert.All(specs, s => Assert.Equal("story-carousel", s.FormatKey));
    }

    [Fact]
    public void ComposeMany_FeedCarousel_AllSpecsHaveSurfaceScopedFormatKey()
    {
        var decorator = new SurfaceScopedCarouselVisualComposer(FeedCarouselDef, new CarouselVisualComposer());
        var payload = MakeCarouselPayload(slideCount: 2);
        var brand = BrandSnapshot.Empty;
        var inferred = new InferenceAccumulator();

        var specs = decorator.ComposeMany(payload, brand, inferred);

        Assert.Equal(2, specs.Count);
        Assert.All(specs, s => Assert.Equal("feed-carousel", s.FormatKey));
    }

    [Fact]
    public void Constructor_NullDef_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new SurfaceScopedCarouselVisualComposer(null!, new CarouselVisualComposer()));

    [Fact]
    public void Constructor_NullInner_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new SurfaceScopedCarouselVisualComposer(StoryCarouselDef, null!));

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static CarouselContent MakeCarouselPayload(int slideCount = 2)
    {
        var slides = Enumerable.Range(1, slideCount)
            .Select(i => new CarouselSlide($"Title {i}", $"Body {i}", null))
            .ToList();

        return new CarouselContent(
            CoverTitle: "Cover",
            Slides: slides,
            CtaText: "CTA",
            CtaLink: null,
            PublicationDescription: null);
    }
}
