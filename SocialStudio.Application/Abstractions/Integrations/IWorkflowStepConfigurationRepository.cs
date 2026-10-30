namespace SocialStudio.Application.Abstractions.Integrations;

public interface IWorkflowStepConfigurationRepository
{
    Task<IReadOnlyList<WorkflowStepConfigurationRecord>> ListAsync(
        Guid organizationId,
        string? workflowKey = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowStepConfigurationRecord>> ListForStepAsync(
        WorkflowStepResolutionRequest request,
        CancellationToken cancellationToken = default);

    Task ReplaceWorkflowAsync(
        Guid organizationId,
        string workflowKey,
        IReadOnlyList<WorkflowStepConfigurationRecord> rows,
        CancellationToken cancellationToken = default);
}

public sealed record WorkflowStepConfigurationRecord(
    Guid Id,
    Guid OrganizationId,
    string WorkflowKey,
    string AnalysisType,
    string StepKey,
    string? OperationKey,
    string? NetworkKey,
    Guid ProviderId,
    string ProviderKey,
    Guid OrganizationIntegrationId,
    string? ModelId,
    string? ActorId,
    int Priority,
    bool IsEnabled,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

