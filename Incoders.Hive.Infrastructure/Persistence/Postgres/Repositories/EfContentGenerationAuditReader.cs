using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF-backed implementation of <see cref="IContentGenerationAuditReader"/> (Change 3, incident job
/// 8757e85a-6acd-49b4-af11-a2d19fbcd3e1). Resolves the job-level audit timeline server-side so the
/// UI never has to join <c>integration_execution_attempts.correlation_id</c> itself.
///
/// <para>
/// OPEN QUESTION resolved (see task report): as of Change 3, the ONLY content-generation code path
/// that writes to <c>social.integration_execution_attempts</c> is the image-generation pipeline
/// (<c>ImageGenerationGateway</c> for provider-level attempts, <c>ClientAestheticImageBackgroundService</c>
/// for per-slide outcomes) — the AI text/copy generation step does not go through
/// <c>IIntegrationExecutionPolicy</c> at all, so it writes no telemetry rows today.
/// Both image-generation row shapes consistently set <c>correlation_id = FormatResultId</c> and
/// <c>correlation_kind = "content-generation/generate-image"</c>; <c>analysis_id</c> is always null for
/// them. The join is therefore: attempt.correlation_id → format_result.id → format_result.job_id → job.id,
/// filtered to that correlation_kind. This holds for every attempt row verified in the live dev DB.
/// </para>
///
/// <para>
/// Change 4 (same incident): the content/copy and designer steps ARE observable — not via
/// <c>integration_execution_attempts</c>, but via <c>platform.usage_events</c>, which every provider
/// call already writes through <c>IUsageEventLedger</c> regardless of whether it also goes through
/// <c>IIntegrationExecutionPolicy</c>. <c>content_generation_jobs.correlation_id</c> equals
/// <c>usage_events.correlation_id</c> for every provider call made while processing that job — verified
/// against the live dev DB for job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1. <see cref="GetJobTimelineAsync"/>
/// merges both sources into one chronological list; see <see cref="ContentGenerationTimelineStep"/> for
/// the double-counting rule applied where the two sources describe the same underlying provider call.
/// </para>
/// </summary>
public sealed class EfContentGenerationAuditReader : IContentGenerationAuditReader
{
    /// <summary>Only telemetry rows written by the content-generation image pipeline carry a
    /// FormatResultId in correlation_id — see the class remarks.</summary>
    private const string ImageGenerationCorrelationKind = "content-generation/generate-image";

    private const string ProviderAttemptCapabilityKey = "ai-image";
    private const string SlideOutcomeCapabilityKey = "generate-image";

    /// <summary>Hard cap on list page size so a long-running org (or a caller passing a huge
    /// pageSize) cannot force an unbounded result set.</summary>
    private const int MaxPageSize = 100;

    /// <summary>
    /// Double-counting guard (see <see cref="ContentGenerationTimelineStep"/> remarks): a usage event
    /// and an <c>ai-image</c> attempt describing the same provider call are written by two different
    /// layers a few hundred milliseconds to a couple of seconds apart (different stopwatch start
    /// points), never minutes apart — sequential provider calls in this pipeline are always separated
    /// by whole seconds of real work, so this tolerance cannot accidentally fold two distinct calls
    /// into one.
    /// </summary>
    private static readonly TimeSpan DuplicateProviderCallTolerance = TimeSpan.FromSeconds(5);

    private readonly AppIdentityDbContext _db;

    public EfContentGenerationAuditReader(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task<ContentGenerationJobTimeline?> GetJobTimelineAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.ContentGenerationJobs
            .AsNoTracking()
            .Include(j => j.FormatResults)
                .ThenInclude(r => r.Assets)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job is null)
        {
            return null;
        }

        var formatResultIds = job.FormatResults.Select(r => r.Id).ToList();

        var attempts = formatResultIds.Count == 0
            ? []
            : await _db.IntegrationExecutionAttempts
                .AsNoTracking()
                .Where(a => a.CorrelationKind == ImageGenerationCorrelationKind
                            && a.CorrelationId != null
                            && formatResultIds.Contains(a.CorrelationId!.Value))
                .OrderBy(a => a.StartedAtUtc)
                .ToListAsync(ct);

        var formatKeyByResultId = job.FormatResults.ToDictionary(r => r.Id, r => r.FormatKey);

        var attemptSteps = attempts
            .Select(a => ToStep(a, formatKeyByResultId))
            .ToList();

        // Change 4: platform.usage_events.correlation_id == content_generation_jobs.correlation_id
        // for every provider call made while processing this job — this is the ONLY source that sees
        // the content/copy and designer steps, which never go through IIntegrationExecutionPolicy and
        // therefore never reach integration_execution_attempts. Not gated on formatResultIds like the
        // attempts query above: a job that dies before persisting any format result (e.g. it fails
        // mid-orchestration while writing copy) still has usage_events rows and must still show them.
        var usageEvents = await _db.UsageEvents
            .AsNoTracking()
            .Where(u => u.CorrelationId == job.CorrelationId)
            .ToListAsync(ct);

        var usageEventSteps = usageEvents
            .Where(u => !OverlapsExistingImageGenerationStep(u, attemptSteps))
            .Select(ToUsageEventStep);

        var steps = attemptSteps
            .Concat(usageEventSteps)
            .OrderBy(s => s.OccurredAtUtc)
            .ToList();

        var formats = job.FormatResults
            .OrderBy(r => r.CreatedAt)
            .Select(ToFormatOutput)
            .ToList();

        // A format that was requested but never reached "Rendered" means the job did not finish
        // persisting everything it generated — whether it failed outright (no row at all) or was
        // interrupted mid-carousel and safeguarded a "PartiallyRendered" checkpoint (Change 1).
        var renderedFormatKeys = job.FormatResults
            .Where(r => string.Equals(r.Status, "Rendered", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.FormatKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var endedWithoutPersistingEverything = job.RequestedFormats
            .Any(f => !renderedFormatKeys.Contains(f));

        double? totalDurationSeconds = job.StartedAt is not null && job.CompletedAt is not null
            ? (job.CompletedAt.Value - job.StartedAt.Value).TotalSeconds
            : null;

        return new ContentGenerationJobTimeline(
            JobId: job.Id,
            OrganizationId: job.OrganizationId,
            ClientId: job.ClientId,
            AnalysisId: job.AnalysisId,
            Status: job.Status,
            StatusReason: job.StatusReason,
            ErrorCode: job.ErrorCode,
            RequestedFormats: job.RequestedFormats,
            CreatedAt: job.CreatedAt,
            StartedAt: job.StartedAt,
            CompletedAt: job.CompletedAt,
            TotalDurationSeconds: totalDurationSeconds,
            EndedWithoutPersistingEverything: endedWithoutPersistingEverything,
            Steps: steps,
            Formats: formats);
    }

    public async Task<ContentGenerationJobListPage> ListJobsAsync(int page, int pageSize, CancellationToken ct)
    {
        var effectivePage = Math.Max(1, page);
        var effectivePageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _db.ContentGenerationJobs
            .AsNoTracking()
            .OrderByDescending(j => j.CreatedAt);

        var totalCount = await query.CountAsync(ct);

        var pageRows = await query
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(j => new
            {
                j.Id,
                j.OrganizationId,
                j.ClientId,
                j.AnalysisId,
                j.Status,
                j.ErrorCode,
                j.CreatedAt,
                j.StartedAt,
                j.CompletedAt,
                RequestedFormatCount = j.RequestedFormats.Count,
                PersistedFormatCount = j.FormatResults.Count(r =>
                    r.Status == "Rendered" || r.Status == "PartiallyRendered"),
                AssetCount = j.FormatResults.SelectMany(r => r.Assets).Count(),
            })
            .ToListAsync(ct);

        var items = pageRows
            .Select(r => new ContentGenerationJobListItem(
                JobId: r.Id,
                OrganizationId: r.OrganizationId,
                ClientId: r.ClientId,
                AnalysisId: r.AnalysisId,
                Status: r.Status,
                ErrorCode: r.ErrorCode,
                CreatedAt: r.CreatedAt,
                StartedAt: r.StartedAt,
                CompletedAt: r.CompletedAt,
                RequestedFormatCount: r.RequestedFormatCount,
                PersistedFormatCount: r.PersistedFormatCount,
                AssetCount: r.AssetCount))
            .ToList();

        return new ContentGenerationJobListPage(items, effectivePage, effectivePageSize, totalCount);
    }

    private static ContentGenerationTimelineStep ToStep(
        IntegrationExecutionAttemptData attempt,
        IReadOnlyDictionary<Guid, string> formatKeyByResultId)
    {
        var formatResultId = attempt.CorrelationId;
        var formatKey = formatResultId is not null && formatKeyByResultId.TryGetValue(formatResultId.Value, out var key)
            ? key
            : null;

        var isSlideOutcome = string.Equals(attempt.CapabilityKey, SlideOutcomeCapabilityKey, StringComparison.OrdinalIgnoreCase);
        var (slideIndex, slideOutcome, slideOutcomeReason) = isSlideOutcome
            ? ParseSlideDiagnostics(attempt.DiagnosticsJson)
            : (null, null, null);

        return new ContentGenerationTimelineStep(
            OccurredAtUtc: attempt.StartedAtUtc,
            StepType: string.Equals(attempt.CapabilityKey, ProviderAttemptCapabilityKey, StringComparison.OrdinalIgnoreCase)
                ? "image-generation"
                : "slide-outcome",
            FormatResultId: formatResultId,
            FormatKey: formatKey,
            ProviderKey: attempt.ProviderKey,
            CapabilityKey: attempt.CapabilityKey,
            AttemptNumber: attempt.AttemptNumber,
            RetryCount: attempt.RetryCount,
            ElapsedMs: attempt.ElapsedMs,
            Succeeded: string.Equals(attempt.Status, "Succeeded", StringComparison.OrdinalIgnoreCase),
            FailureCode: attempt.FailureCode,
            SlideIndex: slideIndex,
            SlideOutcome: slideOutcome,
            SlideOutcomeReason: slideOutcomeReason);
    }

    /// <summary>
    /// Per-slide rows persist <c>{"slideIndex":N,"outcome":"...","reason":"..."}</c> (see
    /// <c>ClientAestheticImageBackgroundService.WriteSuccessTelemetryAsync/WriteCssFallbackTelemetryAsync</c>).
    /// The success shape omits outcome/reason (only slideIndex). Parsed defensively — a malformed or
    /// unexpected shape degrades to nulls rather than failing the whole timeline read.
    /// </summary>
    private static (int? SlideIndex, string? Outcome, string? Reason) ParseSlideDiagnostics(string? diagnosticsJson)
    {
        if (string.IsNullOrWhiteSpace(diagnosticsJson))
        {
            return (null, null, null);
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(diagnosticsJson);
            var root = doc.RootElement;
            int? slideIndex = root.TryGetProperty("slideIndex", out var slideIndexEl) && slideIndexEl.ValueKind == System.Text.Json.JsonValueKind.Number
                ? slideIndexEl.GetInt32()
                : null;
            string? outcome = root.TryGetProperty("outcome", out var outcomeEl) && outcomeEl.ValueKind == System.Text.Json.JsonValueKind.String
                ? outcomeEl.GetString()
                : null;
            string? reason = root.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == System.Text.Json.JsonValueKind.String
                ? reasonEl.GetString()
                : null;
            return (slideIndex, outcome, reason);
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, null, null);
        }
    }

    /// <summary>
    /// True when <paramref name="usageEvent"/> describes the same provider call as one of the
    /// existing <c>"image-generation"</c> steps sourced from <c>integration_execution_attempts</c> —
    /// see the double-counting remarks on <see cref="ContentGenerationTimelineStep"/>. Matched on
    /// provider key plus start-time proximity rather than duration, because the two writers measure
    /// slightly different windows around the same call (see the ~394262ms/~394201ms pair from the
    /// incident job) — comparing where each call STARTED is the stable signal.
    /// </summary>
    private static bool OverlapsExistingImageGenerationStep(
        UsageEventData usageEvent,
        IReadOnlyList<ContentGenerationTimelineStep> attemptSteps)
    {
        foreach (var step in attemptSteps)
        {
            if (step.StepType != "image-generation")
            {
                continue;
            }

            if (!string.Equals(step.ProviderKey, usageEvent.ProviderKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var delta = step.OccurredAtUtc > usageEvent.RequestTimestamp
                ? step.OccurredAtUtc - usageEvent.RequestTimestamp
                : usageEvent.RequestTimestamp - step.OccurredAtUtc;
            if (delta <= DuplicateProviderCallTolerance)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Maps one <c>platform.usage_events</c> row to a <c>"usage-event"</c> timeline step. Unlike the
    /// attempt-sourced steps, usage events are not anchored to a FormatResultId — <see cref="TryExtractFormatKeyFromPipelineStepKey"/>
    /// recovers a best-effort FormatKey from the "prefix:formatKey" convention already used by every
    /// pipeline_step_key the processor writes (e.g. "designer:feed-carousel"); a key with no colon
    /// (e.g. "ai-analysis") yields null, same as an attempt whose correlation could not be resolved.
    /// </summary>
    private static ContentGenerationTimelineStep ToUsageEventStep(UsageEventData u) =>
        new(
            OccurredAtUtc: u.RequestTimestamp,
            StepType: "usage-event",
            FormatResultId: null,
            FormatKey: TryExtractFormatKeyFromPipelineStepKey(u.PipelineStepKey),
            ProviderKey: u.ProviderKey,
            CapabilityKey: u.PipelineStepKey,
            AttemptNumber: u.RetryCount + 1,
            RetryCount: u.RetryCount,
            ElapsedMs: u.DurationMs,
            // usage_events.Status is a lowercase free-form string written by the instrumentation
            // decorators ("succeeded", "failed", "tokens-missing" — the last means the call itself
            // completed but token accounting failed, which is not a call failure for audit purposes).
            // Only "failed" counts as a failed step.
            Succeeded: !string.Equals(u.Status, "failed", StringComparison.OrdinalIgnoreCase),
            FailureCode: u.FailureCode,
            SlideIndex: null,
            SlideOutcome: null,
            SlideOutcomeReason: null,
            Model: u.Model,
            InputTokens: u.InputTokens,
            OutputTokens: u.OutputTokens,
            EstimatedCostUsd: u.EstimatedCostUsd);

    private static string? TryExtractFormatKeyFromPipelineStepKey(string pipelineStepKey)
    {
        var separatorIndex = pipelineStepKey.IndexOf(':');
        return separatorIndex >= 0 && separatorIndex < pipelineStepKey.Length - 1
            ? pipelineStepKey[(separatorIndex + 1)..]
            : null;
    }

    private static ContentGenerationTimelineFormatOutput ToFormatOutput(ContentGenerationFormatResultData r) =>
        new(
            FormatResultId: r.Id,
            FormatKey: r.FormatKey,
            Status: r.Status,
            ErrorCode: r.ErrorCode,
            UpdatedAt: r.UpdatedAt,
            Assets: r.Assets
                .OrderBy(a => a.AssetIndex)
                .Select(a => new ContentGenerationTimelineAsset(
                    AssetId: a.Id,
                    AssetIndex: a.AssetIndex,
                    Filename: a.Filename,
                    MimeType: a.MimeType,
                    SizeBytes: a.SizeBytes,
                    CreatedAt: a.CreatedAt))
                .ToList());
}
