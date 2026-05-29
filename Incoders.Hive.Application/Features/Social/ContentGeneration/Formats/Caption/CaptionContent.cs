namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Caption;

/// <summary>Structured output schema for caption format generation.</summary>
public sealed record CaptionContent(
    string PrimaryCaption,
    IReadOnlyList<string> Alternates,
    IReadOnlyList<string> Hashtags,
    IReadOnlyList<string>? Mentions);
