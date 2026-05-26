namespace Incoders.Hive.Application.Abstractions.Observability;

/// <summary>
/// Immutable DTO describing one provider call for the universal usage ledger.
/// All nullable fields are explicitly permitted to be null when not applicable
/// (e.g. scraping providers have no token counts; cost may be unknown).
/// </summary>
public sealed record UsageEvent(
    Guid OrganizationId,
    Guid? ClientId,
    string WorkflowKey,
    string PipelineStepKey,
    string ProviderKey,
    string? Model,
    string? PromptVersion,
    DateTime RequestTimestamp,
    DateTime CompletedTimestamp,
    int DurationMs,
    int? InputTokens,
    int? OutputTokens,
    decimal? EstimatedCostUsd,
    int? RequestSizeBytes,
    int? ResponseSizeBytes,
    int RetryCount,
    string Status,
    string? FailureCode,
    Guid CorrelationId);
