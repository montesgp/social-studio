using SocialStudio.Domain.Social.SingleAnalyzer;

namespace SocialStudio.Application.Abstractions.Social;

public interface ISingleContentAnalysisRepository
{
    Task AddAsync(
        SingleContentAnalysisRecord analysis,
        CancellationToken cancellationToken = default);

    Task<SingleContentAnalysisRecord?> GetByIdAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(
        Guid analysisId,
        Guid organizationId,
        SingleContentAnalysisStatus status,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default,
        string? failureCode = null,
        string? failureMessageKey = null);

    Task UpdateAsync(
        SingleContentAnalysisRecord analysis,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists analyses owned by a specific user within an organization, newest first.
    /// Each row carries a summary of its most recent content-generation job (if any),
    /// so the UI can show whether content was generated and whether a package is available
    /// for download — without a second round-trip per row.
    /// </summary>
    Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
        Guid organizationId,
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a client's completed (<see cref="SingleContentAnalysisStatus.Analyzed"/>) analyses,
    /// across all owning users, newest first. Used by the Content Bank's lazy materialization
    /// (design §5.3) to find analyses that still need a <c>ContentSet</c> materialized. Default
    /// implementation returns empty — only <c>EfSingleContentAnalysisRepository</c> overrides it;
    /// existing fakes across the test suite keep compiling unchanged.
    /// </summary>
    Task<IReadOnlyList<SingleContentAnalysisRecord>> ListCompletedByClientAsync(
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SingleContentAnalysisRecord>>([]);

    /// <summary>
    /// Finds the newest analysis owned by the user for the same submitted or normalized URL.
    /// Used as a pre-flight duplicate check before queueing a new extraction + AI run.
    /// </summary>
    Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
        Guid organizationId,
        Guid userId,
        string normalizedUrl,
        string sourceUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the newest analysis for the same submitted or normalized URL across the WHOLE
    /// organization — regardless of which user owns it. Apify (and AI) credits are a
    /// per-organization resource, not a per-user one: two different users in the same org
    /// submitting the same URL must not trigger two extraction runs. This is the HARD gate
    /// <see cref="Features.Social.SingleAnalyzer.Analyze.SingleContentAnalyzer.StartAsync"/>
    /// checks before doing any preflight/extraction work — unlike
    /// <see cref="FindLatestByUserAndUrlAsync"/>, which only drives the frontend's advisory
    /// pre-submit lookup.
    /// <para>
    /// Default returns <c>null</c> so existing test fakes compile unchanged; the real EF adapter
    /// overrides it.
    /// </para>
    /// </summary>
    Task<SingleContentAnalysisRecord?> FindLatestByOrganizationAndUrlAsync(
        Guid organizationId,
        string normalizedUrl,
        string sourceUrl,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SingleContentAnalysisRecord?>(null);

    /// <summary>
    /// Lists every analysis, across ALL organizations, currently in a non-terminal status
    /// (anything other than <see cref="SingleContentAnalysisStatus.Analyzed"/> or
    /// <see cref="SingleContentAnalysisStatus.Failed"/>). Used exclusively by
    /// <c>SingleAnalysisJobWatchdog</c> to sweep analyses whose background job was lost (e.g. the
    /// in-memory <c>ChannelSingleAnalysisJobQueue</c> entry dropped on a process restart) — without
    /// this sweep such a record blocks the org-scoped duplicate gate forever with no way to retry.
    /// <para>
    /// Default returns an empty list so existing test fakes compile unchanged; the real EF adapter
    /// overrides it.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SingleContentAnalysisRecord>> ListNonTerminalAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SingleContentAnalysisRecord>>([]);

    /// <summary>
    /// Hard-deletes an analysis owned by the given user. Cascades to:
    /// - integration_execution_attempts (DB-level FK CASCADE on analysis_id)
    /// - content_generation_jobs and their generated_packages (handled here in EF)
    /// Returns the storage refs of any package files that were linked so the caller can
    /// drop them from <see cref="ContentGeneration.IGeneratedPackageStorage"/>.
    /// Returns <c>null</c> when the analysis does not exist or is not owned by the user.
    /// </summary>
    Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
        Guid analysisId,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed record SingleContentAnalysisRecord(
    Guid Id,
    Guid OrganizationId,
    Guid? ClientId,
    Guid UserId,
    string SourceUrl,
    string? NormalizedUrl,
    string? SourcePlatformKey,
    SingleContentAnalysisStatus Status,
    string? ContentSnapshotJson,
    string? AnalysisJson,
    string? ProviderDiagnosticsJson,
    string? FailureCode,
    string? FailureMessageKey,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    int AiAnalysisFailedAttemptCount = 0,
    DateTime? AiAnalysisRetryBlockedAtUtc = null);

public sealed record SingleContentAnalysisHistoryItem(
    Guid Id,
    Guid? ClientId,
    string SourceUrl,
    string? NormalizedUrl,
    string? SourcePlatformKey,
    SingleContentAnalysisStatus Status,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    ContentGenerationSummary? LatestContentGeneration);

public sealed record ContentGenerationSummary(
    Guid JobId,
    string Status,
    bool HasPackage,
    Guid? PackageJobId,
    DateTime? CompletedAt);

public sealed record SingleContentAnalysisHistoryPage(
    IReadOnlyList<SingleContentAnalysisHistoryItem> Items,
    int TotalCount,
    int Page,
    int PageSize);
