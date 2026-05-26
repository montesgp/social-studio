using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// Insert-only EF repository for usage events.
/// Each call to <see cref="AddAsync"/> immediately saves to the database
/// (independent transaction — not part of the unit of work).
/// </summary>
public sealed class EfUsageEventRepository : IUsageEventRepository
{
    private readonly AppIdentityDbContext _db;

    public EfUsageEventRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(UsageEvent evt, CancellationToken cancellationToken)
    {
        var entity = new UsageEventData
        {
            OrganizationId = evt.OrganizationId,
            ClientId = evt.ClientId,
            WorkflowKey = evt.WorkflowKey,
            PipelineStepKey = evt.PipelineStepKey,
            ProviderKey = evt.ProviderKey,
            Model = evt.Model,
            PromptVersion = evt.PromptVersion,
            RequestTimestamp = evt.RequestTimestamp,
            CompletedTimestamp = evt.CompletedTimestamp,
            DurationMs = evt.DurationMs,
            InputTokens = evt.InputTokens,
            OutputTokens = evt.OutputTokens,
            EstimatedCostUsd = evt.EstimatedCostUsd,
            RequestSizeBytes = evt.RequestSizeBytes,
            ResponseSizeBytes = evt.ResponseSizeBytes,
            RetryCount = evt.RetryCount,
            Status = evt.Status,
            FailureCode = evt.FailureCode,
            CorrelationId = evt.CorrelationId,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.UsageEvents.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
