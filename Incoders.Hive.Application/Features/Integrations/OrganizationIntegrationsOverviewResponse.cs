namespace Incoders.Hive.Application.Features.Integrations;

public sealed record OrganizationIntegrationsOverviewResponse(
    Guid OrganizationId,
    string? DefaultAiProviderKey,
    string? DefaultAiModel,
    IReadOnlyList<IntegrationSetupChecklistItemResponse> Checklist,
    IReadOnlyList<OrganizationIntegrationResponse> Integrations);