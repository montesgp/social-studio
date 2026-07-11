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
    BackgroundSourcingPolicy? BackgroundSourcingPolicy = null,
    // WU-5: client own-account handle loaded by the brand-snapshot loader (CAC-CG-04).
    // Null when no own account is registered. Additive — all existing construction sites unchanged.
    string? Handle = null,
    // Issue #70: whether the client signs their own slides with an @handle attribution.
    // Plain boolean, default false. Gates AttributionPolicy.ResolveSignature — additive, all
    // existing construction sites unchanged (default false = not signed).
    bool SignsOwnSlides = false);
