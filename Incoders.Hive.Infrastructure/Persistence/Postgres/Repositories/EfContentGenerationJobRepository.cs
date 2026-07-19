using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

public sealed class EfContentGenerationJobRepository : IContentGenerationJobRepository
{
    /// <summary>
    /// Statuses that end a job's lifecycle. Gap 1 (incident job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1,
    /// Change 5): before this fix <see cref="UpdateStatusAsync"/> never touched CompletedAt at all —
    /// only the success path's <see cref="UpdateManifestAsync"/> set it — so a job that died via
    /// cancellation, timeout, or any early "Failed" write (analysis missing, package build failure,
    /// watchdog reap, …) kept CompletedAt null forever. That made it impossible to compute
    /// TotalDurationSeconds for exactly the jobs an operator most needs to audit. "PartialSuccess" is
    /// kept alongside "Partial" even though nothing currently writes it (see
    /// <see cref="GetLatestByAnalysisAndClientAsync"/>) — cheap forward-compatibility, not dead weight.
    /// </summary>
    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.Ordinal) { "Completed", "Failed", "Partial", "PartialSuccess" };

    private readonly AppIdentityDbContext _db;

    public EfContentGenerationJobRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task InsertAsync(ContentGenerationJob job, CancellationToken ct)
    {
        _db.ContentGenerationJobs.Add(Map(job));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct)
    {
        var entity = await _db.ContentGenerationJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId, ct);

        return entity is null ? null : Map(entity);
    }

    /// <inheritdoc />
    public async Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.ContentGenerationJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return entity is null ? null : Map(entity);
    }

    public async Task UpdateStatusAsync(Guid id, string status, string? statusReason, string? errorCode, CancellationToken ct)
    {
        var entity = await _db.ContentGenerationJobs.FindAsync(new object[] { id }, ct);
        if (entity is null) return;

        // Clamp diagnostic strings to their column widths. status_reason/error_code can carry
        // truncated exception messages whose length is not under our control; an over-length value
        // would abort SaveChanges (22001) and leave the job stuck in "Running" forever.
        entity.Status = StringColumn.Truncate(status, StringColumn.JobStatus)!;
        entity.StatusReason = StringColumn.Truncate(statusReason, StringColumn.JobStatusReason);
        entity.ErrorCode = StringColumn.Truncate(errorCode, StringColumn.JobErrorCode);
        entity.UpdatedAt = DateTime.UtcNow;

        if (status == "Running" && entity.StartedAt is null)
        {
            entity.StartedAt = DateTime.UtcNow;
        }

        // Gap 1: set the completion timestamp the FIRST time the job reaches any terminal state,
        // including cancellation/timeout — not only the success path (which also flows through here,
        // after UpdateManifestAsync already set CompletedAt — the null guard keeps that earlier,
        // more accurate timestamp instead of nudging it forward by the few ms it takes to write status).
        if (TerminalStatuses.Contains(status) && entity.CompletedAt is null)
        {
            entity.CompletedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task SetRegenerationObjectionAsync(Guid id, string? objection, CancellationToken ct)
    {
        var entity = await _db.ContentGenerationJobs.FindAsync(new object[] { id }, ct);
        if (entity is null) return;

        entity.RegenerationObjection = objection;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateManifestAsync(Guid id, string manifestJson, DateTime completedAt, CancellationToken ct)
    {
        var entity = await _db.ContentGenerationJobs.FindAsync(new object[] { id }, ct);
        if (entity is null) return;

        entity.ManifestJson = manifestJson;
        entity.CompletedAt = completedAt;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct)
    {
        // issue #67 follow-up: AnalyzingProfile (first-time ClientAesthetic blocking wait) is a
        // non-terminal, in-progress status just like Running/Queued. It MUST be included here or
        // ContentGenerationJobWatchdog's orphan-recovery and stuck-sweep filters never see rows in
        // that status — the watchdog's own "AnalyzingProfile" filter becomes dead code in
        // production even though it passes with in-memory fakes.
        var entities = await _db.ContentGenerationJobs
            .AsNoTracking()
            .Where(x => x.Status == "Queued" || x.Status == "Running" || x.Status == "AnalyzingProfile")
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        return entities.Select(Map).ToList();
    }

    /// <inheritdoc />
    public async Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(
        Guid analysisId,
        Guid organizationId,
        Guid clientId,
        CancellationToken ct)
    {
        var entity = await _db.ContentGenerationJobs
            .AsNoTracking()
            .Where(x =>
                x.AnalysisId == analysisId
                && x.OrganizationId == organizationId
                && x.ClientId == clientId
                && (x.Status == "Completed" || x.Status == "Partial" || x.Status == "PartialSuccess"))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : Map(entity);
    }

    private static ContentGenerationJobData Map(ContentGenerationJob job) => new()
    {
        Id = job.Id,
        OrganizationId = job.OrganizationId,
        ClientId = job.ClientId,
        AnalysisId = job.AnalysisId,
        RequestedFormats = job.RequestedFormats.ToList(),
        Status = job.Status,
        StatusReason = job.StatusReason,
        ErrorCode = job.ErrorCode,
        BrandSnapshotJson = job.BrandSnapshotJson,
        ManifestJson = job.ManifestJson,
        RegenerationObjection = job.RegenerationObjection,
        CorrelationId = job.CorrelationId,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt,
    };

    private static ContentGenerationJob Map(ContentGenerationJobData e) => new(
        Id: e.Id,
        OrganizationId: e.OrganizationId,
        ClientId: e.ClientId,
        AnalysisId: e.AnalysisId,
        RequestedFormats: e.RequestedFormats,
        Status: e.Status,
        StatusReason: e.StatusReason,
        ErrorCode: e.ErrorCode,
        BrandSnapshotJson: e.BrandSnapshotJson,
        ManifestJson: e.ManifestJson,
        CorrelationId: e.CorrelationId,
        CreatedAt: e.CreatedAt,
        UpdatedAt: e.UpdatedAt,
        StartedAt: e.StartedAt,
        CompletedAt: e.CompletedAt,
        RegenerationObjection: e.RegenerationObjection);
}
