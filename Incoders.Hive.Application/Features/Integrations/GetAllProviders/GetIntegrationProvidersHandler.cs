using Incoders.Hive.Application.Abstractions.Integrations;
using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.GetAllProviders;

public sealed class GetIntegrationProvidersHandler
    : IRequestHandler<GetIntegrationProvidersQuery, IReadOnlyList<IntegrationProviderResponse>>
{
    private readonly IIntegrationProviderCatalogRepository _providers;

    public GetIntegrationProvidersHandler(IIntegrationProviderCatalogRepository providers)
    {
        _providers = providers;
    }

    public async Task<IReadOnlyList<IntegrationProviderResponse>> Handle(
        GetIntegrationProvidersQuery request,
        CancellationToken cancellationToken)
    {
        _ = request;

        var providers = await _providers.ListActiveAsync(cancellationToken);

        return providers
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayName)
            .Select(IntegrationProviderResponse.FromDomain)
            .ToList();
    }
}
