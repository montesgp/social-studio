namespace SocialStudio.Application.Abstractions.Integrations;

/// <summary>
/// Persistence port for the organization-scoped, synced actor catalog.
/// </summary>
public interface IOrganizationActorRepository
{
    /// <summary>Returns only the actors still advertised by the provider at the last sync.</summary>
    Task<IReadOnlyList<OrganizationActorRecord>> ListByProviderAsync(
        Guid organizationId,
        Guid providerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates one row per actor and marks every previously known actor that is absent
    /// from <paramref name="actors"/> as unavailable. Rows are never deleted: a routing entry may
    /// still point at an actor that has since disappeared from the account.
    /// </summary>
    Task UpsertManyAsync(
        IReadOnlyList<OrganizationActorRecord> actors,
        CancellationToken cancellationToken = default);
}

public sealed record OrganizationActorRecord(
    Guid Id,
    Guid OrganizationId,
    Guid OrganizationIntegrationId,
    Guid ProviderId,
    string ProviderKey,
    string ActorId,
    string Name,
    string Username,
    string DisplayName,
    string MetadataJson,
    bool IsOwned,
    bool IsAvailable,
    DateTime SyncedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
