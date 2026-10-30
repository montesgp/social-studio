namespace SocialStudio.Application.Features.Integrations.Models;

public sealed record OrganizationAiModelResponse(
    Guid Id,
    Guid OrganizationId,
    Guid OrganizationIntegrationId,
    string ProviderKey,
    string ModelId,
    string DisplayName,
    bool IsAvailable,
    DateTime SyncedAtUtc);

public sealed record OrganizationAiModelsResponse(
    Guid OrganizationId,
    string ProviderKey,
    DateTime? LastSyncedAtUtc,
    IReadOnlyList<OrganizationAiModelResponse> Models);

