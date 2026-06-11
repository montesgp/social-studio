using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;

namespace Incoders.Hive.Infrastructure.Integrations;

public sealed class IntegrationResolver : IIntegrationResolver
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IOrganizationIntegrationCredentialRepository _credentials;
    private readonly IIntegrationSecretProtector _protector;

    public IntegrationResolver(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IOrganizationIntegrationCredentialRepository credentials,
        IIntegrationSecretProtector protector)
    {
        _providers = providers;
        _integrations = integrations;
        _credentials = credentials;
        _protector = protector;
    }

    public async Task<Result<ResolvedIntegration>> ResolveProviderAsync(
        Guid organizationId,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
        {
            return Result.Failure<ResolvedIntegration>(IntegrationErrors.OrganizationRequired);
        }

        var provider = await _providers.GetByKeyAsync(providerKey, cancellationToken);
        if (provider is null)
        {
            return Result.Failure<ResolvedIntegration>(IntegrationErrors.ProviderNotFound);
        }

        var integration = await _integrations.GetByOrganizationAndProviderAsync(
            organizationId,
            provider.Id,
            cancellationToken);

        if (integration is null)
        {
            if (!provider.SystemDefaultReadOnly)
            {
                return Result.Failure<ResolvedIntegration>(IntegrationErrors.IntegrationNotFound);
            }

            return Result.Success(new ResolvedIntegration(
                organizationId,
                provider.Key,
                provider.DisplayName,
                provider.Category,
                IntegrationConnectionMode.SystemDefault,
                IntegrationStatus.Connected,
                true,
                false,
                null,
                new Dictionary<string, string>()));
        }

        if (integration.Status != IntegrationStatus.Connected)
        {
            return Result.Failure<ResolvedIntegration>(IntegrationErrors.IntegrationNotConnected);
        }

        var credentials = await _credentials.ListByIntegrationIdAsync(integration.Id, cancellationToken);
        var plain = credentials.ToDictionary(
            x => x.Key,
            x => _protector.Unprotect(x.EncryptedValue),
            StringComparer.OrdinalIgnoreCase);

        return Result.Success(new ResolvedIntegration(
            organizationId,
            provider.Key,
            provider.DisplayName,
            provider.Category,
            integration.ConnectionMode,
            integration.Status,
            integration.IsReadOnly,
            integration.SelectedAiModel,
            plain));
    }
}