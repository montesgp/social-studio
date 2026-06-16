namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Story;

/// <summary>Structured output schema for story format generation.</summary>
public sealed record StoryContent(
    string Title,
    string? Subtitle,
    string? Body,
    IReadOnlyList<string> Callouts,
    string CtaText,
    string? BackgroundPrompt,
    string? SignatureText,
    string? LayoutHint,
    string? PublicationDescription = null);
