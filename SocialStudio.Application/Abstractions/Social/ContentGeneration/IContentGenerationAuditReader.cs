namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for the sysadmin-facing content-generation audit read model (Change 3, incident
/// job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1). Aggregates a job, its per-format results/assets, and the
/// execution telemetry already written to <c>social.integration_execution_attempts</c> — but never
/// read by anything — into a chronological timeline an operator can read without opening a log file.
///
/// Implemented in Infrastructure against EF; Application only sees these plain projections.
/// </summary>
public interface IContentGenerationAuditReader
{
    /// <summary>
    /// Returns the full timeline for one job, or null when the job does not exist. No tenant
    /// filtering: this port is only reachable behind the PlatformAdmin (sysadmin) authorization
    /// policy — see the endpoint mapping — which is intentionally allowed to see across
    /// organizations for support/forensics purposes.
    /// </summary>
    Task<ContentGenerationJobTimeline?> GetJobTimelineAsync(Guid jobId, CancellationToken ct);

    /// <summary>
    /// Returns a bounded, most-recent-first page of jobs across all organizations, for the index the
    /// timeline view drills down from. <paramref name="pageSize"/> is clamped by the implementation
    /// so a caller cannot request an unbounded result set.
    /// </summary>
    Task<ContentGenerationJobListPage> ListJobsAsync(int page, int pageSize, CancellationToken ct);
}

/// <summary>One row in the bounded job index (Change 3 list endpoint).</summary>
public sealed record ContentGenerationJobListItem(
    Guid JobId,
    Guid OrganizationId,
    Guid? ClientId,
    Guid AnalysisId,
    string Status,
    string? ErrorCode,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int RequestedFormatCount,
    int PersistedFormatCount,
    int AssetCount);

/// <summary>Bounded, paginated page of <see cref="ContentGenerationJobListItem"/>.</summary>
public sealed record ContentGenerationJobListPage(
    IReadOnlyList<ContentGenerationJobListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// Full per-job audit timeline. <see cref="EndedWithoutPersistingEverything"/> is the explicit marker
/// the owner asked for: true when the job's terminal state left at least one requested format without
/// a fully "Rendered" result — i.e. it ended (failed, cancelled, or timed out) before persisting
/// everything it generated, even if some format(s) safeguarded partial output (Change 1).
/// </summary>
public sealed record ContentGenerationJobTimeline(
    Guid JobId,
    Guid OrganizationId,
    Guid? ClientId,
    Guid AnalysisId,
    string Status,
    string? StatusReason,
    string? ErrorCode,
    IReadOnlyList<string> RequestedFormats,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    double? TotalDurationSeconds,
    bool EndedWithoutPersistingEverything,
    IReadOnlyList<ContentGenerationTimelineStep> Steps,
    IReadOnlyList<ContentGenerationTimelineFormatOutput> Formats);

/// <summary>
/// One chronological step in the timeline. <see cref="StepType"/> is one of:
/// <list type="bullet">
///   <item><description><c>"image-generation"</c> — provider-level attempt, capability_key=ai-image,
///   sourced from <c>social.integration_execution_attempts</c>.</description></item>
///   <item><description><c>"slide-outcome"</c> — per-slide degrade-to-CSS-or-succeed outcome,
///   capability_key=generate-image, sourced from <c>social.integration_execution_attempts</c>.</description></item>
///   <item><description><c>"usage-event"</c> — any other provider call recorded in
///   <c>platform.usage_events</c> for this job (content/copy generation, the designer step, the
///   image-prompt writer, …) — the steps that were previously invisible because they never went
///   through <see cref="SocialStudio.Application.Abstractions.Integrations.IIntegrationExecutionPolicy"/>.
///   For this step type <see cref="CapabilityKey"/> holds <c>usage_events.pipeline_step_key</c> (e.g.
///   <c>"format:carousel"</c>, <c>"designer:feed-carousel"</c>, <c>"ai-analysis"</c>) — the same
///   "what kind of call was this" role <see cref="CapabilityKey"/> plays for the attempt-sourced step
///   types above.</description></item>
/// </list>
/// Resolved server-side to the owning format result (where determinable) so the UI never has to
/// join <c>correlation_id</c> itself.
///
/// <para>
/// DOUBLE-COUNTING (Change 4, incident job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1): a
/// <c>image-background:{formatKey}</c> usage event and an <c>ai-image</c>/comfyui
/// <c>integration_execution_attempts</c> row can describe the SAME underlying provider call — the
/// former is written by the outer <c>ClientAestheticImageBackgroundService</c> scope, the latter by
/// <see cref="SocialStudio.Application.Abstractions.Integrations.IIntegrationExecutionPolicy"/> one
/// layer further in. <see cref="EfContentGenerationAuditReader"/> DEDUPLICATES: a usage-event row is
/// dropped from the merged timeline when an existing <c>"image-generation"</c> step already covers
/// the same provider call (same provider key, request timestamp within a few seconds of the
/// attempt's start) — see <c>EfContentGenerationAuditReader.OverlapsExistingImageGenerationStep</c>.
/// Everything else from <c>usage_events</c> (the prompt-writer call inside the same
/// <c>image-background:*</c> scope, <c>format:*</c>, <c>designer:*</c>, <c>ai-analysis</c>, …) has no
/// counterpart in <c>integration_execution_attempts</c> and is kept.
/// </para>
/// </summary>
public sealed record ContentGenerationTimelineStep(
    DateTime OccurredAtUtc,
    string StepType,
    Guid? FormatResultId,
    string? FormatKey,
    string ProviderKey,
    string CapabilityKey,
    int AttemptNumber,
    int RetryCount,
    int ElapsedMs,
    bool Succeeded,
    string? FailureCode,
    int? SlideIndex,
    string? SlideOutcome,
    string? SlideOutcomeReason,
    string? Model = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    decimal? EstimatedCostUsd = null);

/// <summary>What was actually produced for one requested format: its result status plus every
/// persisted asset row.</summary>
public sealed record ContentGenerationTimelineFormatOutput(
    Guid FormatResultId,
    string FormatKey,
    string Status,
    string? ErrorCode,
    DateTime UpdatedAt,
    IReadOnlyList<ContentGenerationTimelineAsset> Assets);

/// <summary>One persisted asset row (one slide / one file).</summary>
public sealed record ContentGenerationTimelineAsset(
    Guid AssetId,
    int AssetIndex,
    string Filename,
    string MimeType,
    long SizeBytes,
    DateTime CreatedAt);
