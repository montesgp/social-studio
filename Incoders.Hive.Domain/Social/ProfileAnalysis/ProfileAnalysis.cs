namespace Incoders.Hive.Domain.Social.ProfileAnalysis;

/// <summary>
/// Aggregate root representing an Instagram profile analysis run.
/// </summary>
/// <remarks>
/// Architecture constraints (PA-13, PA-REG-01):
/// - ZERO references to EF, Npgsql, System.Text.Json, or AspNetCore types.
///   JSON serialisation of <see cref="RawPostsDatasetJson"/>, <see cref="ServerDerivedStatsJson"/>,
///   <see cref="AiAnalysisJson"/>, and <see cref="ProviderDiagnosticsJson"/> lives exclusively in
///   the Infrastructure layer (same pattern as SingleContentAnalysisData.*Json).
/// - No EF navigation properties. <see cref="ClientId"/> and <see cref="OwnAccountId"/> are
///   soft Guid references (no navigation to SocialClientData / ClientOwnAccountData).
/// - <see cref="OrganizationId"/> is the scoping invariant: all queries must filter by it.
/// </remarks>
public sealed class ProfileAnalysis
{
    // Private constructor — use static factories.
    private ProfileAnalysis() { }

    // ── Identity and scoping ───────────────────────────────────────────────

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    // ── Source: client + own account path (nullable for URL path) ─────────

    /// <summary>Soft reference to the linked client. Null for URL-path analyses.</summary>
    public Guid? ClientId { get; private set; }

    /// <summary>Soft reference to the linked Instagram own account. Null for URL-path analyses.</summary>
    public Guid? OwnAccountId { get; private set; }

    // ── Source: URL path ──────────────────────────────────────────────────

    /// <summary>The submitted Instagram profile URL. Null for client+account path analyses.</summary>
    public string? ProfileUrl { get; private set; }

    // ── Resolved profile handle/URL (always populated) ───────────────────

    /// <summary>
    /// The handle or URL used as the scrape target; populated regardless of which source path was used.
    /// For the client path this is the resolved Instagram handle; for the URL path this is <see cref="ProfileUrl"/>.
    /// </summary>
    public string ProfileUrlOrHandle { get; private set; } = string.Empty;

    // ── Status ────────────────────────────────────────────────────────────

    public ProfileAnalysisStatus Status { get; private set; }

    // ── Persisted JSON payloads (serialised in Infrastructure) ───────────

    /// <summary>JSON snapshot of the raw owner-filtered posts dataset (populated after scrape).</summary>
    public string? RawPostsDatasetJson { get; private set; }

    /// <summary>JSON snapshot of the server-derived stats (populated after metric computation).</summary>
    public string? ServerDerivedStatsJson { get; private set; }

    /// <summary>
    /// JSON snapshot of the profile-level data (ProfileSnapshot) used to reconstruct AI input for a retry.
    /// Populated at the same time as <see cref="RawPostsDatasetJson"/> (after the scrape stage).
    /// Without this, a retry-AI operation cannot reconstruct the AI input without re-scraping.
    /// </summary>
    public string? ProfileSnapshotJson { get; private set; }

    /// <summary>JSON snapshot of the AI analysis result (populated after AI step).</summary>
    public string? AiAnalysisJson { get; private set; }

    /// <summary>JSON snapshot of the provider diagnostics (populated throughout the pipeline).</summary>
    public string? ProviderDiagnosticsJson { get; private set; }

    // ── Failure ───────────────────────────────────────────────────────────

    /// <summary>Failure reason code; populated when <see cref="Status"/> is <see cref="ProfileAnalysisStatus.Failed"/>.</summary>
    public string? FailureCode { get; private set; }

    // ── Timestamps ────────────────────────────────────────────────────────

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Set when the analysis reaches a terminal status (Analyzed or Failed).</summary>
    public DateTime? CompletedAtUtc { get; private set; }

    // ── Domain-model value objects (not persisted directly) ──────────────

    /// <summary>
    /// In-memory AI analysis result; null until <see cref="CompleteAnalysis"/> is called.
    /// Infrastructure is responsible for serialising/deserialising this to/from <see cref="AiAnalysisJson"/>.
    /// </summary>
    public ProfileAiAnalysis? AiAnalysis { get; private set; }

    /// <summary>Warnings emitted during the analysis (low-confidence, sparse-data notices, etc.).</summary>
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    // ── Static factories ──────────────────────────────────────────────────

    /// <summary>
    /// Creates a profile analysis initiated via the client + own account path.
    /// Status is set to <see cref="ProfileAnalysisStatus.Requested"/>.
    /// </summary>
    public static ProfileAnalysis CreateFromClient(
        Guid organizationId,
        Guid clientId,
        Guid ownAccountId,
        string profileUrlOrHandle,
        DateTime createdAtUtc)
    {
        return new ProfileAnalysis
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ClientId = clientId,
            OwnAccountId = ownAccountId,
            ProfileUrl = null,
            ProfileUrlOrHandle = profileUrlOrHandle,
            Status = ProfileAnalysisStatus.Requested,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
    }

    /// <summary>
    /// Creates a profile analysis initiated via the manual URL path.
    /// <see cref="ClientId"/> and <see cref="OwnAccountId"/> are null for URL-path analyses.
    /// Status is set to <see cref="ProfileAnalysisStatus.Requested"/>.
    /// </summary>
    public static ProfileAnalysis CreateFromUrl(
        Guid organizationId,
        string profileUrl,
        DateTime createdAtUtc)
    {
        return new ProfileAnalysis
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ClientId = null,
            OwnAccountId = null,
            ProfileUrl = profileUrl,
            ProfileUrlOrHandle = profileUrl,
            Status = ProfileAnalysisStatus.Requested,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
    }

    // ── Infrastructure reconstitution factory (mapping from persisted data) ─

    /// <summary>
    /// Reconstitutes a <see cref="ProfileAnalysis"/> aggregate from persisted column values.
    /// For use EXCLUSIVELY by Infrastructure persistence mapping — DO NOT call from Application or Domain.
    /// Bypasses the status-machine guard because data is already in a valid persisted state.
    /// </summary>
    public static ProfileAnalysis Reconstitute(
        Guid id,
        Guid organizationId,
        Guid? clientId,
        Guid? ownAccountId,
        string profileUrlOrHandle,
        ProfileAnalysisStatus status,
        string? rawPostsDatasetJson,
        string? serverDerivedStatsJson,
        string? profileSnapshotJson,
        string? aiAnalysisJson,
        string? providerDiagnosticsJson,
        string? failureCode,
        DateTime createdAtUtc,
        DateTime updatedAtUtc,
        DateTime? completedAtUtc)
    {
        return new ProfileAnalysis
        {
            Id = id,
            OrganizationId = organizationId,
            ClientId = clientId,
            OwnAccountId = ownAccountId,
            ProfileUrlOrHandle = profileUrlOrHandle,
            Status = status,
            RawPostsDatasetJson = rawPostsDatasetJson,
            ServerDerivedStatsJson = serverDerivedStatsJson,
            ProfileSnapshotJson = profileSnapshotJson,
            AiAnalysisJson = aiAnalysisJson,
            ProviderDiagnosticsJson = providerDiagnosticsJson,
            FailureCode = failureCode,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc,
            CompletedAtUtc = completedAtUtc,
        };
    }

    // ── Status-transition methods ─────────────────────────────────────────

    /// <summary>Transitions the analysis to <see cref="ProfileAnalysisStatus.ScrapingProfile"/>.</summary>
    public void StartScraping(DateTime updatedAtUtc)
    {
        EnsureTransition(ProfileAnalysisStatus.ScrapingProfile, updatedAtUtc);
    }

    /// <summary>
    /// Transitions to <see cref="ProfileAnalysisStatus.ProfileScraped"/> or
    /// <see cref="ProfileAnalysisStatus.ProfileScrapedPartial"/> based on whether the
    /// scrape produced degraded posts or used the backup actor (PA-08).
    /// </summary>
    /// <param name="isPartial">Set to <c>true</c> when some owner posts were degraded
    ///     or the backup actor was used, so the AI stage can still proceed on partial data.</param>
    public void CompleteScrape(bool isPartial, DateTime updatedAtUtc)
    {
        var target = isPartial
            ? ProfileAnalysisStatus.ProfileScrapedPartial
            : ProfileAnalysisStatus.ProfileScraped;
        EnsureTransition(target, updatedAtUtc);
    }

    /// <summary>Transitions the analysis to <see cref="ProfileAnalysisStatus.Analyzing"/>.</summary>
    public void StartAnalyzing(DateTime updatedAtUtc)
    {
        EnsureTransition(ProfileAnalysisStatus.Analyzing, updatedAtUtc);
    }

    /// <summary>
    /// Transitions the analysis to <see cref="ProfileAnalysisStatus.Analyzed"/> (terminal success)
    /// and attaches the <see cref="ProfileAiAnalysis"/> domain value object.
    /// </summary>
    public void CompleteAnalysis(ProfileAiAnalysis analysis, DateTime completedAtUtc)
    {
        EnsureTransition(ProfileAnalysisStatus.Analyzed, completedAtUtc);
        AiAnalysis = analysis;
        CompletedAtUtc = completedAtUtc;
    }

    /// <summary>
    /// Transitions the analysis to <see cref="ProfileAnalysisStatus.Failed"/> (terminal failure).
    /// Reachable from any non-terminal status.
    /// </summary>
    /// <param name="failureCode">Stable machine-readable failure code (e.g. "Integration.ApifyNotConfigured").</param>
    /// <param name="message">Optional human-readable failure detail (stored in ProviderDiagnosticsJson by Infrastructure).</param>
    public void MarkFailed(string failureCode, string? message, DateTime failedAtUtc)
    {
        EnsureTransition(ProfileAnalysisStatus.Failed, failedAtUtc);
        FailureCode = failureCode;
        CompletedAtUtc = failedAtUtc;
    }

    // ── Data-setter methods (called by Application orchestrator after JSON serialisation) ──

    /// <summary>
    /// Stores the serialised raw posts dataset and server-derived stats JSON.
    /// Called by the orchestrator after the scrape + metric computation stages.
    /// The JSON serialisation stays in the Application layer — Domain stays clean.
    /// </summary>
    public void SetRawData(string rawPostsJson, string statsJson, string diagnosticsJson)
    {
        RawPostsDatasetJson = rawPostsJson;
        ServerDerivedStatsJson = statsJson;
        ProviderDiagnosticsJson = diagnosticsJson;
    }

    /// <summary>
    /// Stores the serialised ProfileSnapshot JSON required to reconstruct AI input for a retry.
    /// Called by the orchestrator at the same time as <see cref="SetRawData"/>.
    /// </summary>
    public void SetProfileSnapshot(string profileSnapshotJson)
    {
        ProfileSnapshotJson = profileSnapshotJson;
    }

    /// <summary>
    /// Re-opens a <see cref="ProfileAnalysisStatus.Failed"/> record to
    /// <see cref="ProfileAnalysisStatus.Analyzing"/> so the AI stage can be re-run
    /// using the already-persisted scrape data (no re-scrape required).
    /// </summary>
    /// <remarks>
    /// Guard invariants:
    /// - The record MUST be in <see cref="ProfileAnalysisStatus.Failed"/> state.
    /// - The record MUST have persisted scrape data (<see cref="RawPostsDatasetJson"/>,
    ///   <see cref="ServerDerivedStatsJson"/>, and <see cref="ProfileSnapshotJson"/> all non-null).
    ///   A Failed record without scrape data means the scrape itself failed — a full re-run is required.
    /// </remarks>
    public void RestartAnalysis(DateTime nowUtc)
    {
        if (Status != ProfileAnalysisStatus.Failed)
        {
            throw new InvalidOperationException(
                $"RestartAnalysis is only valid on a Failed record. " +
                $"Current status: {Status}. AnalysisId={Id}");
        }

        if (string.IsNullOrWhiteSpace(RawPostsDatasetJson) ||
            string.IsNullOrWhiteSpace(ServerDerivedStatsJson) ||
            string.IsNullOrWhiteSpace(ProfileSnapshotJson))
        {
            throw new InvalidOperationException(
                $"RestartAnalysis requires persisted scrape data (RawPostsDatasetJson, " +
                $"ServerDerivedStatsJson, ProfileSnapshotJson). " +
                $"The scrape stage did not complete for AnalysisId={Id}. Use a full re-run instead.");
        }

        Status = ProfileAnalysisStatus.Analyzing;
        FailureCode = null;
        CompletedAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Stores the serialised AI analysis JSON after the AI stage completes.
    /// Called by the orchestrator after CompleteAnalysis transitions the status to Analyzed.
    /// </summary>
    public void SetAiAnalysis(string aiAnalysisJson, string diagnosticsJson)
    {
        AiAnalysisJson = aiAnalysisJson;
        ProviderDiagnosticsJson = diagnosticsJson;
    }

    /// <summary>
    /// Stores the serialised diagnostics JSON on failure paths.
    /// </summary>
    public void SetDiagnostics(string diagnosticsJson)
    {
        ProviderDiagnosticsJson = diagnosticsJson;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private void EnsureTransition(ProfileAnalysisStatus target, DateTime updatedAtUtc)
    {
        if (!ProfileAnalysisStatusTransitions.IsValid(Status, target))
        {
            throw new InvalidOperationException(
                $"Invalid ProfileAnalysis status transition: {Status} → {target}. " +
                $"AnalysisId={Id}");
        }

        Status = target;
        UpdatedAtUtc = updatedAtUtc;
    }
}
