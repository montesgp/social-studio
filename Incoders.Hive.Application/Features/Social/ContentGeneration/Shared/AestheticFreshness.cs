namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;

/// <summary>
/// Shared constants for the aesthetic-data freshness and staleness logic.
/// Refs: design §4, OQ-1; CAG-CG-03.
/// </summary>
public static class AestheticFreshness
{
    /// <summary>
    /// System-wide default staleness threshold in days.
    /// A client-level <c>AestheticRefreshThresholdDays</c> override takes precedence when set.
    /// Effective threshold = <c>client.AestheticRefreshThresholdDays ?? DefaultStalenessThresholdDays</c>.
    /// </summary>
    public const int DefaultStalenessThresholdDays = 30;
}
