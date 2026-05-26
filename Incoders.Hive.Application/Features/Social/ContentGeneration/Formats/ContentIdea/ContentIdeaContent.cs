namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Formats.ContentIdea;

/// <summary>Structured output schema for content idea format generation.</summary>
public sealed record ContentIdeaContent(
    string IdeaTitle,
    string Angle,
    IReadOnlyList<string> Platforms,
    string Rationale,
    double BrandFitScore);
