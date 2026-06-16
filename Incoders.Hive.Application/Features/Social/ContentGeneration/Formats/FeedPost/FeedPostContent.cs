namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.FeedPost;

/// <summary>Structured output schema for feed post format generation.</summary>
public sealed record FeedPostContent(
    string Title,
    string? Subtitle,
    string? Body,
    IReadOnlyList<string> Callouts,
    string CtaText,
    string? BackgroundPrompt,
    string? SignatureText,
    string? LayoutHint,
    string? PublicationDescription = null);
