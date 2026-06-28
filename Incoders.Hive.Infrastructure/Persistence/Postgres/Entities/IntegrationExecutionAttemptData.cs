namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class IntegrationExecutionAttemptData
{
    public Guid Id { get; set; }

    public Guid? AnalysisId { get; set; }

    public Guid? CorrelationId { get; set; }

    public string? CorrelationKind { get; set; }

    public string CapabilityKey { get; set; } = string.Empty;

    public string ProviderKey { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int AttemptNumber { get; set; }

    public string? FailureCode { get; set; }

    public int RetryCount { get; set; }

    public int ElapsedMs { get; set; }

    public string? DiagnosticsJson { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public SingleContentAnalysisData? Analysis { get; set; }
}