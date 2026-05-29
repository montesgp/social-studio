namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of a brand manual background template captured for generation.
/// </summary>
public sealed record BrandBackgroundTemplateSnapshot(
    string Name,
    string Url,
    string? UseCase);
