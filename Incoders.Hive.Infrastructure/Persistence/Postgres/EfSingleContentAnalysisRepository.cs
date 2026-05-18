using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfSingleContentAnalysisRepository : ISingleContentAnalysisRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfSingleContentAnalysisRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(
        SingleContentAnalysisRecord analysis,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        _dbContext.SingleContentAnalyses.Add(ToData(analysis));
        return Task.CompletedTask;
    }

    public async Task<SingleContentAnalysisRecord?> GetByIdAsync(
        Guid analysisId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SingleContentAnalyses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == analysisId && x.OrganizationId == organizationId, cancellationToken);

        return row is null ? null : ToRecord(row);
    }

    public async Task UpdateStatusAsync(
        Guid analysisId,
        Guid organizationId,
        SingleContentAnalysisStatus status,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default,
        string? failureCode = null,
        string? failureMessageKey = null)
    {
        var row = await _dbContext.SingleContentAnalyses
            .FirstOrDefaultAsync(x => x.Id == analysisId && x.OrganizationId == organizationId, cancellationToken);

        if (row is null)
        {
            return;
        }

        row.Status = status.ToString();
        row.UpdatedAtUtc = updatedAtUtc;
        row.FailureCode = failureCode;
        row.FailureMessageKey = failureMessageKey;

        if (status is SingleContentAnalysisStatus.Analyzed or SingleContentAnalysisStatus.Failed)
        {
            row.CompletedAtUtc = updatedAtUtc;
        }
    }

    private static SingleContentAnalysisData ToData(SingleContentAnalysisRecord analysis) => new()
    {
        Id = analysis.Id,
        OrganizationId = analysis.OrganizationId,
        ClientId = analysis.ClientId,
        UserId = analysis.UserId,
        SourceUrl = analysis.SourceUrl,
        NormalizedUrl = analysis.NormalizedUrl,
        SourcePlatformKey = analysis.SourcePlatformKey,
        Status = analysis.Status.ToString(),
        ContentSnapshotJson = analysis.ContentSnapshotJson,
        AnalysisJson = analysis.AnalysisJson,
        ProviderDiagnosticsJson = analysis.ProviderDiagnosticsJson,
        FailureCode = analysis.FailureCode,
        FailureMessageKey = analysis.FailureMessageKey,
        CreatedAtUtc = analysis.CreatedAtUtc,
        UpdatedAtUtc = analysis.UpdatedAtUtc,
        CompletedAtUtc = analysis.CompletedAtUtc,
    };

    private static SingleContentAnalysisRecord ToRecord(SingleContentAnalysisData row) => new(
        row.Id,
        row.OrganizationId,
        row.ClientId,
        row.UserId,
        row.SourceUrl,
        row.NormalizedUrl,
        row.SourcePlatformKey,
        Enum.Parse<SingleContentAnalysisStatus>(row.Status),
        row.ContentSnapshotJson,
        row.AnalysisJson,
        row.ProviderDiagnosticsJson,
        row.FailureCode,
        row.FailureMessageKey,
        row.CreatedAtUtc,
        row.UpdatedAtUtc,
        row.CompletedAtUtc);
}