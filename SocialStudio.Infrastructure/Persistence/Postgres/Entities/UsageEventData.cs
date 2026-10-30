namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

public sealed class UsageEventData
{
    public long Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? ClientId { get; set; }

    public string WorkflowKey { get; set; } = string.Empty;

    public string PipelineStepKey { get; set; } = string.Empty;

    public string ProviderKey { get; set; } = string.Empty;

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public DateTime RequestTimestamp { get; set; }

    public DateTime CompletedTimestamp { get; set; }

    public int DurationMs { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public decimal? EstimatedCostUsd { get; set; }

    public int? RequestSizeBytes { get; set; }

    public int? ResponseSizeBytes { get; set; }

    public int RetryCount { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? FailureCode { get; set; }

    public Guid CorrelationId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
