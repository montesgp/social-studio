using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

public sealed class EfGeneratedPackageRepository : IGeneratedPackageRepository
{
    private readonly AppIdentityDbContext _db;

    public EfGeneratedPackageRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task InsertAsync(GeneratedPackage package, CancellationToken ct)
    {
        _db.GeneratedPackages.Add(Map(package));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct)
    {
        var entity = await _db.GeneratedPackages
            .AsNoTracking()
            .Include(x => x.Job)
            .FirstOrDefaultAsync(x => x.JobId == jobId && x.Job!.OrganizationId == organizationId, ct);

        return entity is null ? null : Map(entity);
    }

    public async Task MarkDownloadedAsync(
        Guid jobId,
        Guid organizationId,
        Guid? userId,
        DateTime downloadedAtUtc,
        CancellationToken ct)
    {
        var entity = await _db.GeneratedPackages
            .Include(x => x.Job)
            .FirstOrDefaultAsync(x => x.JobId == jobId && x.Job!.OrganizationId == organizationId, ct);

        if (entity is null)
        {
            return;
        }

        entity.DownloadCount += 1;
        entity.LastDownloadedAtUtc = downloadedAtUtc;
        entity.LastDownloadedByUserId = userId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkSentToCanvaAsync(
        Guid jobId,
        Guid organizationId,
        Guid? userId,
        DateTime sentAtUtc,
        CancellationToken ct)
    {
        var entity = await _db.GeneratedPackages
            .Include(x => x.Job)
            .FirstOrDefaultAsync(x => x.JobId == jobId && x.Job!.OrganizationId == organizationId, ct);

        if (entity is null)
        {
            return;
        }

        entity.CanvaSendCount += 1;
        entity.LastSentToCanvaAtUtc = sentAtUtc;
        entity.LastSentToCanvaByUserId = userId;
        await _db.SaveChangesAsync(ct);
    }

    private static GeneratedPackageData Map(GeneratedPackage p) => new()
    {
        Id = p.Id,
        JobId = p.JobId,
        StorageUri = p.StorageUri,
        SizeBytes = p.SizeBytes,
        ManifestJson = p.ManifestJson,
        ExpiresAt = p.ExpiresAt,
        CreatedAt = p.CreatedAt,
        DownloadCount = p.DownloadCount,
        LastDownloadedAtUtc = p.LastDownloadedAtUtc,
        LastDownloadedByUserId = p.LastDownloadedByUserId,
        CanvaSendCount = p.CanvaSendCount,
        LastSentToCanvaAtUtc = p.LastSentToCanvaAtUtc,
        LastSentToCanvaByUserId = p.LastSentToCanvaByUserId,
    };

    private static GeneratedPackage Map(GeneratedPackageData e) => new(
        Id: e.Id,
        JobId: e.JobId,
        StorageUri: e.StorageUri,
        SizeBytes: e.SizeBytes,
        ManifestJson: e.ManifestJson,
        ExpiresAt: e.ExpiresAt,
        CreatedAt: e.CreatedAt,
        DownloadCount: e.DownloadCount,
        LastDownloadedAtUtc: e.LastDownloadedAtUtc,
        LastDownloadedByUserId: e.LastDownloadedByUserId,
        CanvaSendCount: e.CanvaSendCount,
        LastSentToCanvaAtUtc: e.LastSentToCanvaAtUtc,
        LastSentToCanvaByUserId: e.LastSentToCanvaByUserId);
}
