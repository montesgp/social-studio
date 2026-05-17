using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using MediatR;
using Incoders.Hive.Application.Features.Integrations;

namespace Incoders.Hive.Application.Features.Integrations.GetOverview;

public sealed class GetOrganizationIntegrationsHandler
    : IRequestHandler<GetOrganizationIntegrationsQuery, Result<OrganizationIntegrationsOverviewResponse>>
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IOrganizationIntegrationCredentialRepository _credentials;

    public GetOrganizationIntegrationsHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IOrganizationIntegrationCredentialRepository credentials)
    {
        _providers = providers;
        _integrations = integrations;
        _credentials = credentials;
    }

    public async Task<Result<OrganizationIntegrationsOverviewResponse>> Handle(
        GetOrganizationIntegrationsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<OrganizationIntegrationsOverviewResponse>(IntegrationErrors.OrganizationRequired);
        }

        var providers = await _providers.ListActiveAsync(cancellationToken);
        var providerMap = providers.ToDictionary(x => x.Id);

        var integrations = await _integrations.ListByOrganizationAsync(request.OrganizationId, cancellationToken);
        var integrationMap = integrations.ToDictionary(x => x.ProviderId);

        var credentialKeyMap = new Dictionary<Guid, IReadOnlyList<string>>();
        foreach (var integration in integrations)
        {
            var credentialKeys = (await _credentials.ListByIntegrationIdAsync(integration.Id, cancellationToken))
                .Select(x => x.Key)
                .OrderBy(x => x)
                .ToList();

            credentialKeyMap[integration.Id.Value] = credentialKeys;
        }

        var responseIntegrations = providers
            .OrderBy(x => x.Category)
            .ThenBy(x => x.DisplayName)
            .Select(provider =>
            {
                integrationMap.TryGetValue(provider.Id, out var integration);
                var keys = integration is null
                    ? Array.Empty<string>()
                    : credentialKeyMap.GetValueOrDefault(integration.Id.Value, Array.Empty<string>());

                return IntegrationResponseFactory.From(request.OrganizationId, provider, integration, keys);
            })
            .ToList();

        var hasAiProvider = responseIntegrations.Any(x =>
            x.Category == IntegrationProviderCategory.AiModel
            && x.Status == IntegrationStatus.Connected);

        var hasCanva = responseIntegrations.Any(x =>
            string.Equals(x.ProviderKey, "canva", StringComparison.OrdinalIgnoreCase)
            && x.Status == IntegrationStatus.Connected);

        var hasBlotato = responseIntegrations.Any(x =>
            string.Equals(x.ProviderKey, "blotato", StringComparison.OrdinalIgnoreCase)
            && x.Status == IntegrationStatus.Connected);

        var checklist = new List<IntegrationSetupChecklistItemResponse>
        {
            new(
                "ai-provider",
                "Connect at least one AI provider",
                true,
                hasAiProvider,
                "At least one AI provider must be connected to run generation and evaluation flows."),
            new(
                "canva",
                "Connect Canva",
                true,
                hasCanva,
                "Canva integration is required for design/canvas workflow steps."),
            new(
                "blotato",
                "Connect Blotato",
                true,
                hasBlotato,
                "Blotato integration is required for publication workflow steps.")
        };

        var defaultAi = responseIntegrations.FirstOrDefault(x =>
            x.Category == IntegrationProviderCategory.AiModel
            && x.IsDefaultAi
            && x.Status == IntegrationStatus.Connected);

        var overview = new OrganizationIntegrationsOverviewResponse(
            request.OrganizationId,
            defaultAi?.ProviderKey,
            defaultAi?.SelectedAiModel,
            checklist,
            responseIntegrations);

        return Result.Success(overview);
    }
}
