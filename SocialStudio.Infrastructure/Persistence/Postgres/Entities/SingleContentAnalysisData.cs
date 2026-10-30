namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

public sealed class SingleContentAnalysisData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? ClientId { get; set; }

    public Guid UserId { get; set; }

    public string SourceUrl { get; set; } = string.Empty;

    public string? NormalizedUrl { get; set; }

    public string? SourcePlatformKey { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? ContentSnapshotJson { get; set; }

    public string? AnalysisJson { get; set; }

    public string? ProviderDiagnosticsJson { get; set; }

    public string? FailureCode { get; set; }

    public string? FailureMessageKey { get; set; }

    /// <summary>
    /// Nullable jsonb column populated forward-only by analyses produced with prompt version v2 or later.
    /// Existing rows keep NULL.
    /// </summary>
    public string? PatternSetJson { get; set; }

    public int AiAnalysisFailedAttemptCount { get; set; }

    public DateTime? AiAnalysisRetryBlockedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public TenantData? Organization { get; set; }

    public ICollection<IntegrationExecutionAttemptData> Attempts { get; set; } = new List<IntegrationExecutionAttemptData>();
}
