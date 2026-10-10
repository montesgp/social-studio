namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Carousel;

/// <summary>
/// A single slide in a carousel post.
/// </summary>
/// <param name="TitleHighlight">
/// client-style-fidelity (CSF-CG-06): the exact words of <paramref name="Title"/> the client would
/// emphasise in their highlight colour. Requested only for clients observed highlighting words.
/// </param>
public sealed record CarouselSlide(
    string Title,
    string Body,
    string? ImagePromptHint,
    string? TitleHighlight = null);

/// <summary>
/// Structured output schema for carousel format generation.
/// This type is the AI response payload; it is pure data with no behaviour.
/// </summary>
/// <param name="CoverTitleHighlight">
/// client-style-fidelity (CSF-CG-06): the exact words of <paramref name="CoverTitle"/> to emphasise.
/// </param>
public sealed record CarouselContent(
    string CoverTitle,
    IReadOnlyList<CarouselSlide> Slides,
    string CtaText,
    string? CtaLink,
    string? PublicationDescription = null,
    IReadOnlyList<string>? Hashtags = null,
    string? CoverTitleHighlight = null);
