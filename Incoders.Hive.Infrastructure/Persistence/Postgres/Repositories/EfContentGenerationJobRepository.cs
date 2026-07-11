using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

public sealed class EfContentGenerationJobRepository : IContentGenerationJobRepository
{
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
