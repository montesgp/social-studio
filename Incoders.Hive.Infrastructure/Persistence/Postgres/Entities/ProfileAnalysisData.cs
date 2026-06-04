namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF entity for the <c>social.profile_analyses</c> table.
/// Maps the <c>ProfileAnalysis</c> domain aggregate.
/// JSON payloads (raw posts, stats, AI analysis, diagnostics) are stored as jsonb columns —
/// same hybrid persistence pattern as <see cref="SingleContentAnalysisData"/>.
/// No EF navigation to <c>SocialClientData</c> or <c>ClientOwnAccountData</c> (soft Guid refs only —
/// matches the sibling-aggregate precedent in SingleContentAnalysisData).
/// </summary>
public sealed class ProfileAnalysisData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    /// <summary>Soft reference to the linked client. Null for URL-path analyses.</summary>
    public Guid? ClientId { get; set; }

    /// <summary>Soft reference to the linked Instagram own account. Null for URL-path analyses.</summary>
    public Guid? OwnAccountId { get; set; }

    /// <summary>The handle or URL used as the scrape target (always populated).</summary>
    public string ProfileUrlOrHandle { get; set; } = string.Empty;

    /// <summary>Status enum value serialised as a string (e.g. "Requested", "Analyzing", "Analyzed").</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// The raw owner-filtered posts dataset serialised as JSON.
    /// Populated after the scrape stage. Stored as jsonb.
    /// </summary>
    public string? RawPostsDatasetJson { get; set; }

    /// <summary>
    /// Server-derived aggregate stats serialised as JSON.
    /// Populated after the metric-computation stage. Stored as jsonb.
    /// </summary>
    public string? ServerDerivedStatsJson { get; set; }

    /// <summary>
    /// Profile-level snapshot (ProfileSnapshot) serialised as JSON.
    /// Populated together with <see cref="RawPostsDatasetJson"/> after the scrape stage.
    /// Required to reconstruct AI input for a retry-AI without re-scraping.
    /// </summary>
    public string? ProfileSnapshotJson { get; set; }

    /// <summary>
    /// AI analysis result serialised as JSON.
    /// Populated after the AI stage. Stored as jsonb.
    /// </summary>
    public string? AiAnalysisJson { get; set; }

    /// <summary>
    /// Provider diagnostics serialised as JSON (actor ids, fallback flags, posts received, etc.).
    /// Populated throughout the pipeline. Stored as jsonb.
    /// </summary>
    public string? ProviderDiagnosticsJson { get; set; }

    /// <summary>Failure reason code; populated when Status = "Failed".</summary>
    public string? FailureCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Set when the analysis reaches a terminal status (Analyzed or Failed).</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Navigation to the owning tenant. Used for FK cascade only — not loaded in queries.</summary>
    public TenantData? Organization { get; set; }
}
