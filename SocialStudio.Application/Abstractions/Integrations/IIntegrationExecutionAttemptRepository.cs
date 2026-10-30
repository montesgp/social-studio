namespace SocialStudio.Application.Abstractions.Integrations;

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
    Guid? AnalysisId,
    string CapabilityKey,
    string ProviderKey,
    IntegrationExecutionAttemptStatus Status,
    int AttemptNumber,
    string? FailureCode,
    int RetryCount,
    int ElapsedMs,
    string? DiagnosticsJson,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    Guid? CorrelationId = null,
    string? CorrelationKind = null);

public enum IntegrationExecutionAttemptStatus
{
    Started = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4,
}
