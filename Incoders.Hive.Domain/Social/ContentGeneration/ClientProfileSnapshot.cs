using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of the client profile captured for content generation.
/// </summary>
public sealed record ClientProfileSnapshot(
    string Name,
    string? Description,
    string? SectorOrNiche,
    // WU-4: policy snapshot enables the processor to resolve the background strategy once per job.
    // Null = Unset = legacy 3-tier behavior (backward-compatible default).
    BackgroundSourcingPolicy? BackgroundSourcingPolicy = null);
