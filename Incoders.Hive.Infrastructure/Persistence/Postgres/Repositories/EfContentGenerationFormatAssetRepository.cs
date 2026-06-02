using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

public sealed class EfContentGenerationFormatAssetRepository : IContentGenerationFormatAssetRepository
{
    private readonly AppIdentityDbContext _db;

    public EfContentGenerationFormatAssetRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task UpsertManyForFormatAsync(
        Guid formatResultId,
        IReadOnlyList<FormatAssetRow> assets,
        CancellationToken ct)
    {
        // Delete-then-insert for idempotent upsert semantics.
        // Ensures retried jobs never produce duplicate asset rows.
        var existing = await _db.ContentGenerationFormatAssets
            .Where(x => x.FormatResultId == formatResultId)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            _db.ContentGenerationFormatAssets.RemoveRange(existing);
        }

        foreach (var asset in assets)
        {
            _db.ContentGenerationFormatAssets.Add(Map(asset));
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A failed asset insert (FK violation, missing table, etc.) leaves pending child
            // entities tracked in this scoped DbContext. Clear them so the caller can still
            // persist a render failure or mark the job failed without retrying the bad insert.
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<IReadOnlyList<FormatAssetRow>> ListByJobAsync(Guid jobId, CancellationToken ct)
    {
        var entities = await _db.ContentGenerationFormatAssets
            .AsNoTracking()
            .Where(x => x.FormatResult != null && x.FormatResult.JobId == jobId)
            .ToListAsync(ct);

        return entities.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisClientAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken ct)
    {
        var entities = await _db.ContentGenerationFormatAssets
            .AsNoTracking()
            .Where(x => x.FormatResult != null
                && x.FormatResult.Job != null
                && x.FormatResult.Job.OrganizationId == organizationId
                && x.FormatResult.Job.AnalysisId == analysisId)
            .ToListAsync(ct);

        return entities.Select(Map).ToList();
    }

    public async Task DeleteByFormatResultIdAsync(Guid formatResultId, CancellationToken ct)
    {
        var existing = await _db.ContentGenerationFormatAssets
            .Where(x => x.FormatResultId == formatResultId)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            _db.ContentGenerationFormatAssets.RemoveRange(existing);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static ContentGenerationFormatAssetData Map(FormatAssetRow r) => new()
    {
        Id = r.Id,
        FormatResultId = r.FormatResultId,
        AssetIndex = r.AssetIndex,
        StoragePath = r.StoragePath,
        Filename = r.Filename,
        MimeType = r.MimeType,
        SizeBytes = r.SizeBytes,
        ContentHash = r.ContentHash,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    private static FormatAssetRow Map(ContentGenerationFormatAssetData e) => new(
        Id: e.Id,
        FormatResultId: e.FormatResultId,
        AssetIndex: e.AssetIndex,
        StoragePath: e.StoragePath,
        Filename: e.Filename,
        MimeType: e.MimeType,
        SizeBytes: e.SizeBytes,
        ContentHash: e.ContentHash,
        CreatedAt: e.CreatedAt,
        UpdatedAt: e.UpdatedAt);
}
