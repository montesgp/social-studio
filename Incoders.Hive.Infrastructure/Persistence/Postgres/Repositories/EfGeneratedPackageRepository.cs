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

    private static GeneratedPackageData Map(GeneratedPackage p) => new()
    {
        Id = p.Id,
        JobId = p.JobId,
        StorageUri = p.StorageUri,
        SizeBytes = p.SizeBytes,
        ManifestJson = p.ManifestJson,
        ExpiresAt = p.ExpiresAt,
        CreatedAt = p.CreatedAt,
    };

    private static GeneratedPackage Map(GeneratedPackageData e) => new(
        Id: e.Id,
        JobId: e.JobId,
        StorageUri: e.StorageUri,
        SizeBytes: e.SizeBytes,
        ManifestJson: e.ManifestJson,
        ExpiresAt: e.ExpiresAt,
        CreatedAt: e.CreatedAt);
}
