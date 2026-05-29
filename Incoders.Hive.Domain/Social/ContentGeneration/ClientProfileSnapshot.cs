namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of the client profile captured for content generation.
/// </summary>
public sealed record ClientProfileSnapshot(
    string Name,
    string? Description,
    string? SectorOrNiche);
