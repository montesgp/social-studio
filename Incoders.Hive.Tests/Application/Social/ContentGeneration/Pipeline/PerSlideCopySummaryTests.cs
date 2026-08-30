using Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// Unit coverage for <see cref="ContentGenerationJobProcessor.TryGetPerSlideCopySummaries"/>: the
/// per-slide copy threaded into image generation so each background is a metaphor of that slide's
/// message. Prefers the copy AI's ImagePromptHint, else title + body.
/// </summary>
public sealed class PerSlideCopySummaryTests
{
    [Fact]
    public void CarouselContent_PrefersImagePromptHint_WhenPresent()
    {
        var content = new CarouselContent(
            CoverTitle: "Cover",
            Slides:
            [
                new CarouselSlide("Empezá terapia", "No sabés por dónde", "an open door letting warm light in"),
            ],
            CtaText: "Empezá acá",
            CtaLink: null);

        var result = ContentGenerationJobProcessor.TryGetPerSlideCopySummaries(content);

        Assert.Single(result);
        Assert.Equal("an open door letting warm light in", result[0]);
    }

    [Fact]
    public void CarouselContent_FallsBackToTitleAndBody_WhenNoHint()
    {
        var content = new CarouselContent(
            CoverTitle: "Cover",
            Slides:
            [
                new CarouselSlide("Empezá terapia", "No sabés por dónde", ImagePromptHint: null),
            ],
            CtaText: "Empezá acá",
            CtaLink: null);

        var result = ContentGenerationJobProcessor.TryGetPerSlideCopySummaries(content);

        Assert.Single(result);
        Assert.Equal("Empezá terapia. No sabés por dónde", result[0]);
    }

    [Fact]
    public void UnknownPayload_ReturnsEmpty()
    {
        var result = ContentGenerationJobProcessor.TryGetPerSlideCopySummaries(new { irrelevant = true });

        Assert.Empty(result);
    }
}
