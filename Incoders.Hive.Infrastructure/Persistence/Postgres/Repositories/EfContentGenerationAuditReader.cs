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
/// OPEN QUESTION resolved (see task report): as of this change, the ONLY content-generation code path
/// that writes to <c>social.integration_execution_attempts</c> is the image-generation pipeline
/// (<c>ImageGenerationGateway</c> for provider-level attempts, <c>ClientAestheticImageBackgroundService</c>
/// for per-slide outcomes) — the AI text/copy generation step does not go through
/// <c>IIntegrationExecutionPolicy</c> at all, so it writes no telemetry rows today.
/// Both image-generation row shapes consistently set <c>correlation_id = FormatResultId</c> and
/// <c>correlation_kind = "content-generation/generate-image"</c>; <c>analysis_id</c> is always null for
/// them. The join is therefore: attempt.correlation_id → format_result.id → format_result.job_id → job.id,
/// filtered to that correlation_kind. This holds for every attempt row verified in the live dev DB.
/// <c>content_generation_jobs.correlation_id</c> exists but is currently unused by any telemetry writer —
/// no migration was needed to answer this question; a job-level correlation_id write path only becomes
/// necessary if/when a non-image content-generation step starts calling IIntegrationExecutionPolicy.
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

        var steps = attempts
            .Select(a => ToStep(a, formatKeyByResultId))
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
