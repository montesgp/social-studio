using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfIntegrationExecutionAttemptRepository : IIntegrationExecutionAttemptRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfIntegrationExecutionAttemptRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(
        IntegrationExecutionAttemptRecord attempt,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        _dbContext.IntegrationExecutionAttempts.Add(ToData(attempt));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<IntegrationExecutionAttemptRecord>> ListByAnalysisIdAsync(
        Guid analysisId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.IntegrationExecutionAttempts
            .AsNoTracking()
            .Where(x => x.AnalysisId == analysisId)
            .OrderBy(x => x.AttemptNumber)
            .ThenBy(x => x.StartedAtUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(ToRecord).ToList();
    }

    private static IntegrationExecutionAttemptData ToData(IntegrationExecutionAttemptRecord attempt) => new()
    {
        Id = attempt.Id,
        AnalysisId = attempt.AnalysisId,
        CapabilityKey = attempt.CapabilityKey,
        ProviderKey = attempt.ProviderKey,
        Status = attempt.Status.ToString(),
        AttemptNumber = attempt.AttemptNumber,
        FailureCode = attempt.FailureCode,
        RetryCount = attempt.RetryCount,
        ElapsedMs = attempt.ElapsedMs,
        DiagnosticsJson = attempt.DiagnosticsJson,
        StartedAtUtc = attempt.StartedAtUtc,
        CompletedAtUtc = attempt.CompletedAtUtc,
    };

    private static IntegrationExecutionAttemptRecord ToRecord(IntegrationExecutionAttemptData row) => new(
        row.Id,
        row.AnalysisId,
        row.CapabilityKey,
        row.ProviderKey,
        Enum.Parse<IntegrationExecutionAttemptStatus>(row.Status),
        row.AttemptNumber,
        row.FailureCode,
        row.RetryCount,
        row.ElapsedMs,
        row.DiagnosticsJson,
        row.StartedAtUtc,
        row.CompletedAtUtc);
}