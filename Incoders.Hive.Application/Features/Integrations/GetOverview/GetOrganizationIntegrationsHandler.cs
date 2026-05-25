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

        var hasContentDataProvider = responseIntegrations.Any(x =>
            x.Category == IntegrationProviderCategory.DataExtraction
            && (string.Equals(x.ProviderKey, "apify", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.ProviderKey, "data365", StringComparison.OrdinalIgnoreCase))
            && x.Status == IntegrationStatus.Connected);

        var singleAnalyzerChecklist = new List<IntegrationSetupChecklistItemResponse>
        {
            new(
                "single-analyzer-data-provider",
                "Connect Apify or Data365",
                true,
                hasContentDataProvider,
                "At least one content data provider must be connected to resolve social content."),
            new(
                "single-analyzer-ai-provider",
                "Connect at least one AI model provider",
                true,
                hasAiProvider,
                "At least one AI model provider must be connected to analyze resolved content.")
        };

        var generatorChecklist = new List<IntegrationSetupChecklistItemResponse>
        {
            new(
                "generator-data-provider",
                "Connect Apify or Data365",
                true,
                hasContentDataProvider,
                "At least one content data provider must be connected before generated ideas can use source content."),
            new(
                "generator-ai-provider",
                "Connect at least one AI model provider",
                true,
                hasAiProvider,
                "At least one AI model provider must be connected to generate content ideas.")
        };

        var checklistGroups = new List<IntegrationSetupChecklistResponse>
        {
            new("single-analyzer", "Single Analyzer", singleAnalyzerChecklist),
            new("generator", "Generator required setup checklist", generatorChecklist)
        };

        var checklist = singleAnalyzerChecklist.Concat(generatorChecklist).ToList();

        var defaultAi = responseIntegrations.FirstOrDefault(x =>
            x.Category == IntegrationProviderCategory.AiModel
            && x.IsDefaultAi
            && x.Status == IntegrationStatus.Connected);

        var overview = new OrganizationIntegrationsOverviewResponse(
            request.OrganizationId,
            defaultAi?.ProviderKey,
            defaultAi?.SelectedAiModel,
            checklist,
            checklistGroups,
            responseIntegrations);

        return Result.Success(overview);
    }
}
