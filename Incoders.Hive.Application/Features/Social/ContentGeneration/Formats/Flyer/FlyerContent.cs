namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.Flyer;

/// <summary>Structured output schema for flyer format generation.</summary>
public sealed record FlyerContent(
    string Title,
    string? Subtitle,
    string? Body,
    IReadOnlyList<string> Callouts,
    string CtaText,
    string? LayoutHint);
