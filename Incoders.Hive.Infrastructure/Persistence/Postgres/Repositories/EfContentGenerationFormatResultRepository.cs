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

    public async Task UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct)
    {
        var existing = await _db.ContentGenerationFormatResults
            .FirstOrDefaultAsync(x => x.JobId == result.JobId && x.FormatKey == result.FormatKey, ct);

        if (existing is null)
        {
            _db.ContentGenerationFormatResults.Add(Map(result));
        }
        else
        {
            existing.Status = result.Status;
            existing.OutputJson = result.OutputJson;
            existing.CompositionSpecJson = result.CompositionSpecJson;
            existing.RenderedAssetUri = result.RenderedAssetUri;
            existing.InferredFieldsJson = result.InferredFieldsJson;
            existing.PromptVersion = result.PromptVersion;
            existing.ErrorCode = result.ErrorCode;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct)
    {
        var entities = await _db.ContentGenerationFormatResults
            .AsNoTracking()
            .Where(x => x.JobId == jobId)
            .ToListAsync(ct);

        return entities.Select(Map).ToList();
    }

    private static ContentGenerationFormatResultData Map(ContentGenerationFormatResult r) => new()
    {
        Id = r.Id,
        JobId = r.JobId,
        FormatKey = r.FormatKey,
        Status = r.Status,
        OutputJson = r.OutputJson,
        CompositionSpecJson = r.CompositionSpecJson,
        RenderedAssetUri = r.RenderedAssetUri,
        InferredFieldsJson = r.InferredFieldsJson,
        PromptVersion = r.PromptVersion,
        ErrorCode = r.ErrorCode,
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
