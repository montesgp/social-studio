using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Abstractions.Integrations;

public interface IWorkflowConfigurationResolver
{
    Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
        WorkflowStepResolutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record WorkflowStepResolutionRequest(
    Guid OrganizationId,
    string WorkflowKey,
    string AnalysisType,
    string StepKey,
    string? OperationKey = null,
    string? NetworkKey = null);

public sealed record WorkflowStepRouteCandidate(
    Guid ProviderId,
    string ProviderKey,
    Guid OrganizationIntegrationId,
    IntegrationConnectionMode ConnectionMode,
    int Priority,
    string? ModelId,
    string? ActorId,
    bool IsEnabled)
{
    /// <summary>
    /// Attempts allowed per provider before falling back to the next candidate.
    /// </summary>
    /// <remarks>
    /// This was 1, which disabled retries entirely: <c>ProviderFallbackPolicy</c> only retries while
    /// <c>AttemptNumber &lt; MaxAttempts</c>, so a failure classified as retryable (provider
    /// unavailable, timeout, 429, 5xx) was never actually retried — a provider being restarted for a
    /// few seconds permanently lost the work. Two attempts means exactly one retry: enough to ride
    /// out a transient blip, bounded enough that a slow capability cannot blow the job budget.
    /// Raising this further is NOT free — image generation can take ~8 minutes per attempt against a
    /// 30-minute job budget, so extra attempts must be justified per capability, not globally.
    /// </remarks>
    public const int DefaultMaxAttempts = 2;

    public IntegrationRouteCandidate ToIntegrationRouteCandidate() =>
        new(
            ProviderId,
            ProviderKey,
            ConnectionMode,
            Priority,
            AllowManagedFallback: ConnectionMode != IntegrationConnectionMode.BringYourOwnKey,
            MaxAttempts: DefaultMaxAttempts,
            TimeoutMs: 120_000,
            SelectedModel: ModelId,
            IsEnabled);
}

