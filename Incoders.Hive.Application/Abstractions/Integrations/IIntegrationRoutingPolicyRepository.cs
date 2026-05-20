using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Abstractions.Integrations;

public interface IIntegrationRoutingPolicyRepository
{
    Task<IReadOnlyList<IntegrationRoutingPolicy>> ListByOrganizationAsync(
        Guid organizationId,
        string? flowKey = null,
        string? capabilityKey = null,
        string? platformKey = null,
        CancellationToken cancellationToken = default);

    Task<IntegrationRoutingPolicy?> GetEnabledPolicyAsync(
        Guid organizationId,
        string flowKey,
        string capabilityKey,
        string? platformKey,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        IntegrationRoutingPolicy policy,
        CancellationToken cancellationToken = default);
}

public interface IIntegrationExecutionAttemptRepository
{
    Task AddAsync(
        IntegrationExecutionAttemptRecord attempt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IntegrationExecutionAttemptRecord>> ListByAnalysisIdAsync(
        Guid analysisId,
        CancellationToken cancellationToken = default);
}

public sealed record IntegrationExecutionAttemptRecord(
    Guid Id,
    Guid AnalysisId,
    string CapabilityKey,
    string ProviderKey,
    IntegrationExecutionAttemptStatus Status,
    int AttemptNumber,
    string? FailureCode,
    int RetryCount,
    int ElapsedMs,
    string? DiagnosticsJson,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc);

public enum IntegrationExecutionAttemptStatus
{
    Started = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4,
}
