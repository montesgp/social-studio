namespace Incoders.Hive.Domain.Social.Clients;

/// <summary>
/// Determines how background images are sourced during content generation for a client.
/// Stored nullable on <see cref="Client"/>: null (or Unset) = legacy 3-tier behavior.
/// Refs: BSP-07; design §3.1.
/// </summary>
public enum BackgroundSourcingPolicy
{
    /// <summary>
    /// Legacy 3-tier behavior (ForcedDefault → References → Creative).
    /// Equivalent to a null policy value — no explicit choice has been made.
    /// </summary>
    Unset = 0,

    /// <summary>
    /// CASE A: use the client's defined background set.
    /// One background → strict (same for all slides).
    /// Multiple backgrounds → deterministic rotation seeded by job id.
    /// </summary>
    ClientDefinedSet = 1,

    /// <summary>
    /// CASE B: fully AI-generated backgrounds honoring the brand color palette
    /// (weighted by ColorRole ordinal + Rank). No client background set used.
    /// </summary>
    AiPalette = 2,

    /// <summary>
    /// CASE C: aesthetic guidance from profile-analysis text descriptors
    /// (DetectedBackgroundImages). Degrades to AI-creative when no analysis exists.
    /// </summary>
    ClientAesthetic = 3,
}
