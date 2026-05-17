using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Application.Abstractions.Integrations;

public interface IIntegrationProviderCatalogRepository
{
    Task<IReadOnlyList<IntegrationProvider>> ListActiveAsync(CancellationToken cancellationToken = default);

    Task<IntegrationProvider?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<IntegrationProvider?> GetByIdAsync(IntegrationProviderId id, CancellationToken cancellationToken = default);

    Task UpsertAsync(IntegrationProvider provider, CancellationToken cancellationToken = default);
}