using SocialStudio.Domain.Integrations;

namespace SocialStudio.Application.Abstractions.Integrations;

public interface IOrganizationIntegrationRepository
{
    Task<OrganizationIntegration?> GetByOrganizationAndProviderAsync(
        Guid organizationId,
        IntegrationProviderId providerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAndCategoryAsync(
        Guid organizationId,
        IntegrationProviderCategory category,
        CancellationToken cancellationToken = default);

    Task AddAsync(OrganizationIntegration integration, CancellationToken cancellationToken = default);

    void Update(OrganizationIntegration integration);

    void Remove(OrganizationIntegration integration);
}