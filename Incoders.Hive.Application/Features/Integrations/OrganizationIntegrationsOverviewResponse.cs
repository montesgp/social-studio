namespace Incoders.Hive.Application.Features.Integrations;

public sealed record OrganizationIntegrationsOverviewResponse(
    Guid OrganizationId,
    IReadOnlyList<IntegrationSetupChecklistItemResponse> Checklist,
    IReadOnlyList<IntegrationSetupChecklistResponse> ChecklistGroups,
    IReadOnlyList<OrganizationIntegrationResponse> Integrations);
