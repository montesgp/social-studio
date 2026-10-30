namespace SocialStudio.Application.Features.Integrations.Actors;

/// <param name="Slug">
/// "username/name" — the addressable form the connector already stores in its routing map, so the
/// picker can offer catalog entries without invalidating what an operator configured by hand.
/// </param>
public sealed record OrganizationActorResponse(
    Guid Id,
    Guid OrganizationId,
    Guid OrganizationIntegrationId,
    string ProviderKey,
    string ActorId,
    string Name,
    string Username,
    string Slug,
    string DisplayName,
    bool IsOwned,
    bool IsAvailable,
    DateTime SyncedAtUtc);

public sealed record OrganizationActorsResponse(
    Guid OrganizationId,
    string ProviderKey,
    DateTime? LastSyncedAtUtc,
    IReadOnlyList<OrganizationActorResponse> Actors);
