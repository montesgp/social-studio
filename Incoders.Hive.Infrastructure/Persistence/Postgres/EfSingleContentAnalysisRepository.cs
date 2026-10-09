using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
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

    public async Task UpdateAsync(
        SingleContentAnalysisRecord analysis,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SingleContentAnalyses
            .FirstOrDefaultAsync(
                x => x.Id == analysis.Id && x.OrganizationId == analysis.OrganizationId,
                cancellationToken);

        if (row is null)
        {
            return;
        }

        row.ClientId = analysis.ClientId;
        row.UserId = analysis.UserId;
        row.SourceUrl = analysis.SourceUrl;
        row.NormalizedUrl = analysis.NormalizedUrl;
        row.SourcePlatformKey = analysis.SourcePlatformKey;
        row.Status = analysis.Status.ToString();
        row.ContentSnapshotJson = analysis.ContentSnapshotJson;
        row.AnalysisJson = analysis.AnalysisJson;
        row.ProviderDiagnosticsJson = analysis.ProviderDiagnosticsJson;
        row.FailureCode = analysis.FailureCode;
        row.FailureMessageKey = analysis.FailureMessageKey;
        row.AiAnalysisFailedAttemptCount = analysis.AiAnalysisFailedAttemptCount;
        row.AiAnalysisRetryBlockedAtUtc = analysis.AiAnalysisRetryBlockedAtUtc;
        row.CreatedAtUtc = analysis.CreatedAtUtc;
        row.UpdatedAtUtc = analysis.UpdatedAtUtc;
        row.CompletedAtUtc = analysis.CompletedAtUtc;
    }

    public async Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
        Guid organizationId,
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize switch
        {
            < 1 => 20,
            > 100 => 100,
            _ => pageSize,
        };

        var baseQuery = _dbContext.SingleContentAnalyses
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.UserId == userId);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        // Pull each analysis + the most recent content-generation job (if any) in a single
        // round-trip. EF translates the correlated `OrderByDescending().FirstOrDefault()` into
        // a LATERAL subquery on Postgres.
        var rows = await baseQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(a => new
            {
                a.Id,
                a.ClientId,
                a.SourceUrl,
                a.NormalizedUrl,
                a.SourcePlatformKey,
                a.Status,
                a.FailureCode,
                a.CreatedAtUtc,
                a.UpdatedAtUtc,
                a.CompletedAtUtc,
                LatestJob = _dbContext.ContentGenerationJobs
                    .Where(j => j.AnalysisId == a.Id)
                    .OrderByDescending(j => j.CreatedAt)
                    .Select(j => new
                    {
                        j.Id,
                        j.Status,
                        j.CompletedAt,
                        HasPackage = _dbContext.GeneratedPackages.Any(p => p.JobId == j.Id),
                    })
                    .FirstOrDefault(),
                PackageJobId = _dbContext.ContentGenerationJobs
                    .Where(j => j.AnalysisId == a.Id && _dbContext.GeneratedPackages.Any(p => p.JobId == j.Id))
                    .OrderByDescending(j => j.CreatedAt)
                    .Select(j => (Guid?)j.Id)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new SingleContentAnalysisHistoryItem(
                r.Id,
                r.ClientId,
                r.SourceUrl,
                r.NormalizedUrl,
                r.SourcePlatformKey,
                Enum.Parse<SingleContentAnalysisStatus>(r.Status),
                r.FailureCode,
                r.CreatedAtUtc,
                r.UpdatedAtUtc,
                r.CompletedAtUtc,
                r.LatestJob is null
                    ? null
                    : new ContentGenerationSummary(
                        r.LatestJob.Id,
                        r.LatestJob.Status,
                        r.PackageJobId != null,
                        r.PackageJobId,
                        r.LatestJob.CompletedAt)))
            .ToList();

        return new SingleContentAnalysisHistoryPage(items, totalCount, safePage, safePageSize);
    }

    public async Task<IReadOnlyList<SingleContentAnalysisRecord>> ListCompletedByClientAsync(
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var completedStatus = SingleContentAnalysisStatus.Analyzed.ToString();

        var rows = await _dbContext.SingleContentAnalyses
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ClientId == clientId && x.Status == completedStatus)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(ToRecord).ToList();
    }

    public async Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
        Guid organizationId,
        Guid userId,
        string normalizedUrl,
        string sourceUrl,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SingleContentAnalyses
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.UserId == userId
                && (
                    x.SourceUrl == sourceUrl
                    || x.SourceUrl == normalizedUrl
                    || x.NormalizedUrl == normalizedUrl
                    || x.NormalizedUrl == sourceUrl))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(a => new
            {
                a.Id,
                a.ClientId,
                a.SourceUrl,
                a.NormalizedUrl,
                a.SourcePlatformKey,
                a.Status,
                a.FailureCode,
                a.CreatedAtUtc,
                a.UpdatedAtUtc,
                a.CompletedAtUtc,
                LatestJob = _dbContext.ContentGenerationJobs
                    .Where(j => j.AnalysisId == a.Id)
                    .OrderByDescending(j => j.CreatedAt)
                    .Select(j => new
                    {
                        j.Id,
                        j.Status,
                        j.CompletedAt,
                        HasPackage = _dbContext.GeneratedPackages.Any(p => p.JobId == j.Id),
                    })
                    .FirstOrDefault(),
                PackageJobId = _dbContext.ContentGenerationJobs
                    .Where(j => j.AnalysisId == a.Id && _dbContext.GeneratedPackages.Any(p => p.JobId == j.Id))
                    .OrderByDescending(j => j.CreatedAt)
                    .Select(j => (Guid?)j.Id)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new SingleContentAnalysisHistoryItem(
                row.Id,
                row.ClientId,
                row.SourceUrl,
                row.NormalizedUrl,
                row.SourcePlatformKey,
                Enum.Parse<SingleContentAnalysisStatus>(row.Status),
                row.FailureCode,
                row.CreatedAtUtc,
                row.UpdatedAtUtc,
                row.CompletedAtUtc,
                row.LatestJob is null
                    ? null
                    : new ContentGenerationSummary(
                        row.LatestJob.Id,
                        row.LatestJob.Status,
                        row.PackageJobId != null,
                        row.PackageJobId,
                        row.LatestJob.CompletedAt));
    }

    public async Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
        Guid analysisId,
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var analysis = await _dbContext.SingleContentAnalyses
            .FirstOrDefaultAsync(
                x => x.Id == analysisId
                  && x.OrganizationId == organizationId
                  && x.UserId == userId,
                cancellationToken);

        if (analysis is null)
        {
            return null;
        }

        // content_generation_jobs.analysis_id is indexed but NOT an FK, so EF won't cascade
        // automatically — load and detach explicitly. format_results and generated_packages
        // FK CASCADE off the job, so those rows go away with their parent.
        var jobs = await _dbContext.ContentGenerationJobs
            .Where(j => j.AnalysisId == analysisId)
            .Include(j => j.Package)
            .ToListAsync(cancellationToken);

        var storageUris = jobs
            .Select(j => j.Package?.StorageUri)
            .Where(uri => !string.IsNullOrWhiteSpace(uri))
            .Select(uri => uri!)
            .ToList();

        _dbContext.ContentGenerationJobs.RemoveRange(jobs);
        _dbContext.SingleContentAnalyses.Remove(analysis);

        return storageUris;
    }

    public async Task<SingleContentAnalysisRecord?> FindLatestByOrganizationAndUrlAsync(
        Guid organizationId,
        string normalizedUrl,
        string sourceUrl,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SingleContentAnalyses
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && (
                    x.SourceUrl == sourceUrl
                    || x.SourceUrl == normalizedUrl
                    || x.NormalizedUrl == normalizedUrl
                    || x.NormalizedUrl == sourceUrl))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<SingleContentAnalysisRecord>> ListNonTerminalAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.SingleContentAnalyses
            .AsNoTracking()
            .Where(x => x.Status != "Analyzed" && x.Status != "Failed")
            .ToListAsync(cancellationToken);

        return rows.Select(ToRecord).ToList();
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
        AiAnalysisFailedAttemptCount = analysis.AiAnalysisFailedAttemptCount,
        AiAnalysisRetryBlockedAtUtc = analysis.AiAnalysisRetryBlockedAtUtc,
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
        row.CompletedAtUtc,
        row.AiAnalysisFailedAttemptCount,
        row.AiAnalysisRetryBlockedAtUtc);
}
