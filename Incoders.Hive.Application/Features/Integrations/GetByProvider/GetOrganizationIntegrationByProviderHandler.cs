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

    public GetOrganizationIntegrationByProviderHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IOrganizationIntegrationCredentialRepository credentials)
    {
        _providers = providers;
        _integrations = integrations;
        _credentials = credentials;
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

        IReadOnlyList<string> configuredCredentialKeys = integration is null
            ? Array.Empty<string>()
            : (await _credentials.ListByIntegrationIdAsync(integration.Id, cancellationToken))
                .Select(x => x.Key)
                .OrderBy(x => x)
                .ToList();

        return Result.Success(IntegrationResponseFactory.From(
            request.OrganizationId,
            provider,
            integration,
            configuredCredentialKeys));
    }
}
