using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Formats;

/// <summary>
/// S2-T3 / SCENARIO-18: SurfaceScopedCarouselGenerator decorator tests.
/// Verifies the decorator correctly overrides FormatKey while delegating generation/version to inner.
/// </summary>
public sealed class SurfaceScopedCarouselGeneratorTests
{
    [Fact]
    public void FormatKey_StoryCarouselDecorator_ReturnsStoryCarousel()
    {
        var inner = MakeCarouselGenerator();
        var decorator = new SurfaceScopedCarouselGenerator("story-carousel", inner);

        Assert.Equal("story-carousel", decorator.FormatKey);
    }

    [Fact]
    public void FormatKey_FeedCarouselDecorator_ReturnsFeedCarousel()
    {
        var inner = MakeCarouselGenerator();
        var decorator = new SurfaceScopedCarouselGenerator("feed-carousel", inner);

        Assert.Equal("feed-carousel", decorator.FormatKey);
    }

    [Fact]
    public void PromptVersion_DelegatesToInner_ReturnsCarouselV2()
    {
        // ADR-7: both surface-scoped carousel keys reuse the same version as the inner carousel generator.
        // Latest inner version: carousel.v7 (in-call post caption — mandatory publicationDescription + hashtags[]).
        var inner = MakeCarouselGenerator();
        var decorator = new SurfaceScopedCarouselGenerator("story-carousel", inner);

        Assert.Equal(inner.PromptVersion, decorator.PromptVersion);
        Assert.Equal("carousel.v7", decorator.PromptVersion);
    }

    [Fact]
    public void PromptVersion_FeedCarouselDecorator_DelegatesToInner()
    {
        var inner = MakeCarouselGenerator();
        var decorator = new SurfaceScopedCarouselGenerator("feed-carousel", inner);

        Assert.Equal(inner.PromptVersion, decorator.PromptVersion);
    }

    [Fact]
    public void Constructor_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SurfaceScopedCarouselGenerator("story-carousel", null!));
    }

    [Fact]
    public void Constructor_NullOrEmptyFormatKey_Throws()
    {
        var inner = MakeCarouselGenerator();
        Assert.Throws<ArgumentException>(() =>
            new SurfaceScopedCarouselGenerator("", inner));
    }

    private static CarouselGenerator MakeCarouselGenerator() =>
        new CarouselGenerator(
            new StubAiGateway(),
            new ProviderCallContext());
}

file sealed class StubAiGateway : IAiCompletionGateway
{
    public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
        AiJsonCompletionRequest request, CancellationToken ct) where TResponse : notnull =>
        throw new NotImplementedException("Stub — not called in unit tests");
}
