using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class EfIntegrationExecutionAttemptRepository : IIntegrationExecutionAttemptRepository
{
    private readonly AppIdentityDbContext _dbContext;
    private readonly IServiceScopeFactory _scopeFactory;

    public EfIntegrationExecutionAttemptRepository(
        AppIdentityDbContext dbContext,
        IServiceScopeFactory scopeFactory)
    {
        _dbContext = dbContext;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Persists a telemetry attempt row on a dedicated, short-lived context committed in its
    /// own transaction. The write is intentionally NOT enqueued on the caller's (scoped)
    /// context: telemetry is best-effort observability and must never poison the caller's
    /// unit of work. By saving here, any failure surfaces inside this call (so the caller's
    /// try/catch can swallow it) instead of being deferred to an unrelated later SaveChanges
    /// on the shared content-generation context.
    /// </summary>
    public async Task AddAsync(
        IntegrationExecutionAttemptRecord attempt,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
        context.IntegrationExecutionAttempts.Add(ToData(attempt));
        await context.SaveChangesAsync(cancellationToken);
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
        CorrelationId = attempt.CorrelationId,
        CorrelationKind = attempt.CorrelationKind,
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
        row.CompletedAtUtc,
        CorrelationId: row.CorrelationId,
        CorrelationKind: row.CorrelationKind);
}