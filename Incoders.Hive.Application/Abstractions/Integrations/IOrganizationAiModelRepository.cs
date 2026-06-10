namespace Incoders.Hive.Application.Abstractions.Integrations;

public interface IOrganizationAiModelRepository
{
    Task<IReadOnlyList<OrganizationAiModelRecord>> ListByProviderAsync(
        Guid organizationId,
        Guid providerId,
        CancellationToken cancellationToken = default);

    Task UpsertManyAsync(
        IReadOnlyList<OrganizationAiModelRecord> models,
        CancellationToken cancellationToken = default);
}

public sealed record OrganizationAiModelRecord(
    Guid Id,
    Guid OrganizationId,
    Guid OrganizationIntegrationId,
    Guid ProviderId,
    string ProviderKey,
    string ModelId,
    string DisplayName,
    string CapabilitiesJson,
    string MetadataJson,
    bool IsAvailable,
    DateTime SyncedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

