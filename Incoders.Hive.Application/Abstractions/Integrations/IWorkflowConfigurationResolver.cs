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
    public IntegrationRouteCandidate ToIntegrationRouteCandidate() =>
        new(
            ProviderId,
            ProviderKey,
            ConnectionMode,
            Priority,
            AllowManagedFallback: ConnectionMode != IntegrationConnectionMode.BringYourOwnKey,
            MaxAttempts: 1,
            TimeoutMs: 120_000,
            SelectedModel: ModelId,
            IsEnabled);
}

