using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Application.Abstractions.Social;

/// <summary>
/// Repository port for <see cref="ProfileAnalysis"/> aggregate persistence.
/// All queries are scoped to <paramref name="organizationId"/> — the org-scoping invariant
/// mirrors the single-analyzer repository pattern.
/// Implemented exclusively in Infrastructure (EF / Postgres). No provider types leak here.
/// </summary>
public interface IProfileAnalysisRepository
{
    Task AddAsync(
        ProfileAnalysis analysis,
        CancellationToken cancellationToken = default);

    Task<ProfileAnalysis?> GetByIdAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paged, newest-first list of analyses scoped to <paramref name="organizationId"/>.
    /// </summary>
    Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(
        Guid organizationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        ProfileAnalysis analysis,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes the analysis. Returns <c>true</c> when deleted; <c>false</c> when not found
    /// or not owned by the organization.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the MAX <c>CompletedAtUtc</c> of all <c>Analyzed</c> analyses for the given client,
    /// or <c>null</c> when no completed analysis exists.
    /// Used by <c>ClientAestheticEnsureFreshService</c> to determine whether the cached aesthetic
    /// data is still within the effective staleness threshold (CAG-PA-04).
    /// </summary>
    Task<DateTime?> GetLatestAnalyzedCompletedAtAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when at least one analysis for the given client is in a non-terminal
    /// state (i.e. <c>Status NOT IN ("Analyzed", "Failed")</c> — Requested, Scraping,
    /// ProfileScraped, or Analyzing). Used as the in-flight debounce probe (OQ-8, CAG-PA-05):
    /// if an analysis is already in flight, a new refresh MUST NOT be enqueued.
    /// </summary>
    Task<bool> HasNonTerminalAnalysisForClientAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when the most-recent <c>Analyzed</c> analysis for the client carries a
    /// MATERIALLY USABLE aesthetic payload — non-empty <c>aesthetic_profile_json</c> AND real
    /// aesthetic content (ColorPairings and/or BackgroundPattern), not merely a non-null/non-empty
    /// column value (issue #84). A stale/near-empty analysis (e.g. only PromptVersion +
    /// ReferenceImages populated) returns <c>false</c>, forcing the caller onto the blocking
    /// <c>EnsureAvailableBlockingAsync</c> wait instead of the fast fire-and-forget refresh path.
    /// Used so ClientAesthetic freshness requires real aesthetic data, not just a recent timestamp
    /// or a technically-present JSON blob (CAG2). Default <c>true</c> keeps existing fakes/behavior
    /// unchanged; the EF adapter returns the real, richness-checked value.
    /// </summary>
    Task<bool> LatestAnalyzedHasAestheticAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <summary>
    /// Phase 2 (CAG2-PA-04): persists the enriched aesthetic profile JSON onto an existing analysis
    /// (the optional <c>aesthetic_profile_json</c> column). Self-contained write (commits on its own)
    /// — invoked as a best-effort enrichment step after the analysis is already Analyzed. No-op when
    /// the analysis is not found.
    /// <para>
    /// Default no-op so existing test fakes compile unchanged; the real EF adapter overrides it.
    /// </para>
    /// </summary>
    Task SetAestheticProfileJsonAsync(
        Guid analysisId,
        Guid organizationId,
        string aestheticProfileJson,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// aesthetic-freshness-versioning: returns the schema-version + completeness signal for the
    /// latest completed (<c>Analyzed</c>) analysis's aesthetic payload. Used by
    /// <c>ClientAestheticEnsureFreshService</c> (via <c>AestheticFreshness.Evaluate</c>) to decide
    /// whether an AGE-fresh analysis still needs a refresh — either because it was captured with an
    /// older extractor schema version, or because it never became materially complete.
    /// <para>
    /// Default returns an empty/oldest snapshot (null version, all pillars false) so existing test
    /// fakes compile unchanged; the real EF adapter parses the actual stored value via
    /// <c>AestheticProfilePersistence.Read</c>.
    /// </para>
    /// </summary>
    Task<AestheticFreshnessSnapshot> GetLatestAestheticFreshnessAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AestheticFreshnessSnapshot(null, false, false, false));

    /// <summary>
    /// client-analysis-data admin panel: returns a lightweight summary of the client's most
    /// recent profile analysis (any status, newest by <c>CreatedAtUtc</c> first) — the analysis
    /// id, status, completion timestamp, scrape target, and raw stored aesthetic payload. Used by
    /// the admin panel to show "what was captured last" and, when Analyzed, to locate the row to
    /// patch on edit. Returns <c>null</c> when the client has no analysis at all.
    /// <para>
    /// Default returns <c>null</c> so existing test fakes compile unchanged; the real EF adapter
    /// overrides it.
    /// </para>
    /// </summary>
    Task<LatestProfileAnalysisSummary?> GetLatestForClientAsync(
        Guid orgId,
        Guid clientId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<LatestProfileAnalysisSummary?>(null);
}

/// <summary>
/// client-analysis-data admin panel: lightweight projection of the client's most recent
/// profile analysis row, used to drive the admin read/edit panel without loading the full
/// <c>ProfileAnalysis</c> aggregate.
/// </summary>
public sealed record LatestProfileAnalysisSummary(
    Guid AnalysisId,
    string Status,
    DateTime? CompletedAtUtc,
    string ProfileUrlOrHandle,
    string? AestheticProfileJson);

/// <summary>
/// Freshness signal for the latest completed analysis's aesthetic payload
/// (aesthetic-freshness-versioning). <see cref="SchemaVersion"/> is the stored
/// <c>"aesthetic-extractor/vN"</c> token (null on old/missing blobs). The three <c>Has*</c> flags
/// mark the "materially complete" pillars — the client's registered color combinations, the
/// image/flat background mix, and the visual references — the must-haves for faithful generation.
/// </summary>
public sealed record AestheticFreshnessSnapshot(
    string? SchemaVersion,
    bool HasColorPairings,
    bool HasBackgroundPattern,
    bool HasReferenceImages);

/// <summary>A single row in the profile-analysis history list.</summary>
public sealed record ProfileAnalysisHistoryItem(
    Guid Id,
    Guid OrganizationId,
    Guid? ClientId,
    Guid? OwnAccountId,
    string? ClientName,
    string ProfileUrlOrHandle,
    string Status,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc);

/// <summary>Paged list result for <see cref="IProfileAnalysisRepository.GetByOrganizationAsync"/>.</summary>
public sealed record ProfileAnalysisHistoryPage(
    IReadOnlyList<ProfileAnalysisHistoryItem> Items,
    int TotalCount,
    int Page,
    int PageSize);
