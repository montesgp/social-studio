namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.discovery_runs</c> — the one NicheDiscovery table with UPDATEs (the
/// status lifecycle, design §9). <see cref="SourceMarketCode"/>/<see cref="TargetMarketCode"/> store the
/// canonical <c>MarketCode.Value</c> string form; the Domain aggregate re-parses it via
/// <c>MarketCode.TryParse</c> on load, so a malformed persisted value can never resurrect silently.
/// </summary>
public sealed class DiscoveryRunData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid RequestedByUserId { get; set; }

    public string SourceMarketCode { get; set; } = string.Empty;

    public string TargetMarketCode { get; set; } = string.Empty;

    public string NetworkKey { get; set; } = string.Empty;

    public string ContentFormat { get; set; } = string.Empty;

    /// <summary>The Domain aggregate's <c>DiscoveryRun.ConfigurationSnapshotJson</c> — the resolved
    /// origin mapping, depth ceiling, and other execution parameters snapshotted at run creation
    /// (ND-01). Distinct from <see cref="WeightsJson"/>, which is specifically the scorer weight set
    /// (N7, design §7.1).</summary>
    public string ConfigurationSnapshotJson { get; set; } = string.Empty;

    public int WindowDays { get; set; }

    public short Depth { get; set; }

    public short Status { get; set; }

    /// <summary>The scorer weights snapshotted at run start (N7) — the effective weight set the run
    /// was actually ranked with, independent of any later change to <c>OpportunityScoreWeights.Default</c>.</summary>
    public string WeightsJson { get; set; } = string.Empty;

    public List<string> OriginSourceKeys { get; set; } = [];

    public List<string> UnconfiguredSourceKeys { get; set; } = [];

    public List<string> FailedSourceKeys { get; set; } = [];

    public int ProviderUnitsSpent { get; set; }

    public int AiCompletionsSpent { get; set; }

    /// <summary>Set when the semantic layer fell back to <c>LexicalClusterFallback</c> (N11) — the run
    /// still ranks, but this is surfaced so the operator knows the AI naming/grouping degraded.</summary>
    public bool SemanticDegraded { get; set; }

    /// <summary>Populated when <see cref="Status"/> is <c>Failed</c>.</summary>
    public string? FailureReasonKey { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
