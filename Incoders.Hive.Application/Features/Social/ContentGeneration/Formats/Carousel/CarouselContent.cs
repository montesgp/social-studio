namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;

/// <summary>
/// A single slide in a carousel post.
/// </summary>
public sealed record CarouselSlide(
    string Title,
    string Body,
    string? ImagePromptHint);

/// <summary>
/// Structured output schema for carousel format generation.
/// This type is the AI response payload; it is pure data with no behaviour.
/// </summary>
public sealed record CarouselContent(
    string CoverTitle,
    IReadOnlyList<CarouselSlide> Slides,
    string CtaText,
    string? CtaLink,
    string? PublicationDescription = null,
    IReadOnlyList<string>? Hashtags = null);
