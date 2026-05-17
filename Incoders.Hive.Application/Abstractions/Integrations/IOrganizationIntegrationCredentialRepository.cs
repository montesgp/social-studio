using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Abstractions.Integrations;

public interface IOrganizationIntegrationCredentialRepository
{
    Task<IReadOnlyList<OrganizationIntegrationCredential>> ListByIntegrationIdAsync(
        OrganizationIntegrationId integrationId,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(OrganizationIntegrationCredential credential, CancellationToken cancellationToken = default);

    Task RemoveByIntegrationIdAsync(
        OrganizationIntegrationId integrationId,
        CancellationToken cancellationToken = default);
}