using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

public sealed class EfContentGenerationFormatResultRepository : IContentGenerationFormatResultRepository
{
    private readonly AppIdentityDbContext _db;

    public EfContentGenerationFormatResultRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct)
    {
        var existing = await _db.ContentGenerationFormatResults
            .FirstOrDefaultAsync(x => x.JobId == result.JobId && x.FormatKey == result.FormatKey, ct);

        if (existing is null)
        {
            existing = Map(result);
            _db.ContentGenerationFormatResults.Add(existing);
        }
        else
        {
            existing.Status = StringColumn.Truncate(result.Status, StringColumn.FormatStatus)!;
            existing.OutputJson = result.OutputJson;
            existing.CompositionSpecJson = result.CompositionSpecJson;
            existing.RenderedAssetUri = StringColumn.Truncate(result.RenderedAssetUri, StringColumn.RenderedAssetUri);
            existing.InferredFieldsJson = result.InferredFieldsJson;
            existing.PromptVersion = StringColumn.Truncate(result.PromptVersion, StringColumn.FormatPromptVersion);
            // error_code is the column that overflowed in production: a full exception message
            // (~195 chars) was written here. Clamp so the failure can always be persisted.
            existing.ErrorCode = StringColumn.Truncate(result.ErrorCode, StringColumn.FormatErrorCode);
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        return Map(existing);
    }

    public async Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct)
    {
        var entities = await _db.ContentGenerationFormatResults
            .AsNoTracking()
            .Where(x => x.JobId == jobId)
            .ToListAsync(ct);

        return entities.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ContentGenerationFormatResult>> ListByIdsAsync(
        IReadOnlySet<Guid> resultIds,
        CancellationToken ct)
    {
        if (resultIds.Count == 0)
        {
            return [];
        }

        var entities = await _db.ContentGenerationFormatResults
            .AsNoTracking()
            .Where(x => resultIds.Contains(x.Id))
            .ToListAsync(ct);

        return entities.Select(Map).ToList();
    }

    private static ContentGenerationFormatResultData Map(ContentGenerationFormatResult r) => new()
    {
        Id = r.Id,
        JobId = r.JobId,
        // Clamp bounded columns so an over-length value never aborts the save (22001).
        FormatKey = StringColumn.Truncate(r.FormatKey, StringColumn.FormatKey)!,
        Status = StringColumn.Truncate(r.Status, StringColumn.FormatStatus)!,
        OutputJson = r.OutputJson,
        CompositionSpecJson = r.CompositionSpecJson,
        RenderedAssetUri = StringColumn.Truncate(r.RenderedAssetUri, StringColumn.RenderedAssetUri),
        InferredFieldsJson = r.InferredFieldsJson,
        PromptVersion = StringColumn.Truncate(r.PromptVersion, StringColumn.FormatPromptVersion),
        ErrorCode = StringColumn.Truncate(r.ErrorCode, StringColumn.FormatErrorCode),
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    private static ContentGenerationFormatResult Map(ContentGenerationFormatResultData e) => new(
        Id: e.Id,
        JobId: e.JobId,
        FormatKey: e.FormatKey,
        Status: e.Status,
        OutputJson: e.OutputJson,
        CompositionSpecJson: e.CompositionSpecJson,
        RenderedAssetUri: e.RenderedAssetUri,
        InferredFieldsJson: e.InferredFieldsJson,
        PromptVersion: e.PromptVersion,
        ErrorCode: e.ErrorCode,
        CreatedAt: e.CreatedAt,
        UpdatedAt: e.UpdatedAt);
}
