using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;

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

public sealed record WorkflowStepCatalogResponse(
    string Key,
    string Label,
    string OperationKey,
    string OperationLabel,
    string Description,
    bool RequiresNetwork,
    IReadOnlyList<string> SupportedNetworkKeys,
    IReadOnlyList<IntegrationProviderCategory> AllowedProviderCategories,
    IReadOnlyList<string> AllowedProviderKeys,
    bool SupportsModelSelection,
    bool SupportsActorSelection,
    string? ActorVariantKey);

public sealed record WorkflowAnalysisTypeCatalogResponse(
    string Key,
    string Label,
    IReadOnlyList<WorkflowStepCatalogResponse> Steps);

public sealed record WorkflowCatalogResponse(
    string Key,
    string Label,
    IReadOnlyList<WorkflowAnalysisTypeCatalogResponse> AnalysisTypes);

public sealed record WorkflowConfigurationCatalogResponse(
    IReadOnlyList<WorkflowCatalogResponse> Workflows);

public sealed record WorkflowConfigurationOverviewResponse(
    Guid OrganizationId,
    string? WorkflowKey,
    WorkflowConfigurationCatalogResponse Catalog,
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
    public static WorkflowConfigurationCatalogResponse ToCatalogResponse() =>
        new(WorkflowConfigurationCatalog.Workflows.Select(ToCatalogResponse).ToList());

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

    private static WorkflowCatalogResponse ToCatalogResponse(WorkflowDefinition workflow) =>
        new(
            workflow.Key,
            workflow.Label,
            workflow.AnalysisTypes.Select(ToCatalogResponse).ToList());

    private static WorkflowAnalysisTypeCatalogResponse ToCatalogResponse(WorkflowAnalysisTypeDefinition analysisType) =>
        new(
            analysisType.Key,
            analysisType.Label,
            analysisType.Steps.Select(ToCatalogResponse).ToList());

    private static WorkflowStepCatalogResponse ToCatalogResponse(WorkflowStepDefinition step) =>
        new(
            step.Key,
            step.Label,
            step.OperationKey,
            step.OperationLabel,
            step.Description,
            step.RequiresNetwork,
            step.SupportedNetworkKeys,
            step.AllowedProviderCategories,
            step.AllowedProviderKeys,
            step.SupportsModelSelection,
            step.SupportsActorSelection,
            step.ActorVariantKey);
}
