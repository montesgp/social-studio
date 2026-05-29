namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of a client-associated social account captured for content generation.
/// </summary>
public sealed record ClientAccountSnapshot(
    string NetworkCode,
    string NetworkName,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes);
