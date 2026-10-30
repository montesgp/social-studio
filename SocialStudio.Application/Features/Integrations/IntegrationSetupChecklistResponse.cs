namespace SocialStudio.Application.Features.Integrations;

public sealed record IntegrationSetupChecklistResponse(
    string Key,
    string Label,
    IReadOnlyList<IntegrationSetupChecklistItemResponse> Items);
