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

    public GetOrganizationIntegrationByProviderHandler(
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

        return Result.Success(IntegrationResponseFactory.From(
            request.OrganizationId,
            provider,
            integration,
            configuredCredentialKeys,
            credentialValues));
    }
}
