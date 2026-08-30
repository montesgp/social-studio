using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.GetByProvider;

public sealed class GetOrganizationIntegrationByProviderHandler
    : IRequestHandler<GetOrganizationIntegrationByProviderQuery, Result<OrganizationIntegrationResponse>>
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IOrganizationIntegrationCredentialRepository _credentials;
    private readonly IIntegrationSecretProtector _protector;
    private readonly IOrganizationAiModelRepository? _models;
    private readonly IExternalProviderClientFactory? _clientFactory;

    public GetOrganizationIntegrationByProviderHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IOrganizationIntegrationCredentialRepository credentials,
        IIntegrationSecretProtector protector,
        IOrganizationAiModelRepository? models = null,
        IExternalProviderClientFactory? clientFactory = null)
    {
        _providers = providers;
        _integrations = integrations;
        _credentials = credentials;
        _protector = protector;
        _models = models;
        _clientFactory = clientFactory;
    }

    public async Task<Result<OrganizationIntegrationResponse>> Handle(
        GetOrganizationIntegrationByProviderQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<OrganizationIntegrationResponse>(IntegrationErrors.OrganizationRequired);
        }

        var provider = await _providers.GetByKeyAsync(request.ProviderKey, cancellationToken);
        if (provider is null)
        {
            return Result.Failure<OrganizationIntegrationResponse>(IntegrationErrors.ProviderNotFound);
        }

        var integration = await _integrations.GetByOrganizationAndProviderAsync(
            request.OrganizationId,
            provider.Id,
            cancellationToken);

        var credentials = integration is null
            ? Array.Empty<OrganizationIntegrationCredential>()
            : await _credentials.ListByIntegrationIdAsync(integration.Id, cancellationToken);

        var configuredCredentialKeys = credentials
            .Select(x => x.Key)
            .OrderBy(x => x)
            .ToList();

        var credentialValues = credentials.ToDictionary(
            x => x.Key,
            x => _protector.Unprotect(x.EncryptedValue),
            StringComparer.OrdinalIgnoreCase);

        // The detail view returns the provider's PRIMARY-category card, and the synced catalog maps to
        // that category, so pull synced models whenever they exist (any catalog-capable provider —
        // text AI models OR media backends such as ComfyUI), not only AiModel providers.
        var syncedModels = _models is not null
            ? await _models.ListByProviderAsync(request.OrganizationId, provider.Id.Value, cancellationToken)
            : [];

        var availableModels = syncedModels.Count == 0
            ? provider.AvailableModels
            : syncedModels.Select(x => x.ModelId).ToList();

        var supportsModelSync = _clientFactory?.Supports<IAiModelCatalogClient>(provider.Key) ?? false;

        return Result.Success(IntegrationResponseFactory.From(
            request.OrganizationId,
            provider,
            integration,
            configuredCredentialKeys,
            credentialValues,
            availableModels,
            supportsModelSync: supportsModelSync));
    }
}
