using Incoders.Hive.Application.Abstractions.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.WorkflowConfigurations;

public sealed record WorkflowStepConfigurationResponse(
    Guid Id,
    Guid OrganizationId,
    string WorkflowKey,
    string AnalysisType,
    string StepKey,
    string? OperationKey,
    string? NetworkKey,
    string ProviderKey,
    Guid OrganizationIntegrationId,
    string? ModelId,
    string? ActorId,
    int Priority,
    bool IsEnabled,
    DateTime UpdatedAtUtc);

public sealed record WorkflowConfigurationOverviewResponse(
    Guid OrganizationId,
    string? WorkflowKey,
    IReadOnlyList<WorkflowStepConfigurationResponse> Steps);

public sealed record WorkflowStepConfigurationInput(
    string AnalysisType,
    string StepKey,
    string? OperationKey,
    string? NetworkKey,
    string ProviderKey,
    string? ModelId,
    string? ActorId,
    int Priority,
    bool IsEnabled);

internal static class WorkflowConfigurationResponseMapper
{
    public static WorkflowStepConfigurationResponse ToResponse(WorkflowStepConfigurationRecord row) =>
        new(
            row.Id,
            row.OrganizationId,
            row.WorkflowKey,
            row.AnalysisType,
            row.StepKey,
            row.OperationKey,
            row.NetworkKey,
            row.ProviderKey,
            row.OrganizationIntegrationId,
            row.ModelId,
            row.ActorId,
            row.Priority,
            row.IsEnabled,
            row.UpdatedAtUtc);
}

