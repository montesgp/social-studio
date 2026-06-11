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
    private readonly IOrganizationAiModelRepository? _models;

    public GetOrganizationIntegrationsHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IOrganizationIntegrationCredentialRepository credentials,
        IOrganizationAiModelRepository? models = null)
    {
        _providers = providers;
        _integrations = integrations;
        _credentials = credentials;
        _models = models;
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

        var responseIntegrations = new List<OrganizationIntegrationResponse>();
        foreach (var provider in providers.OrderBy(x => x.Category).ThenBy(x => x.DisplayName))
        {
            integrationMap.TryGetValue(provider.Id, out var integration);
            var keys = integration is null
                ? Array.Empty<string>()
                : credentialKeyMap.GetValueOrDefault(integration.Id.Value, Array.Empty<string>());

            var availableModels = await ResolveAvailableModelsAsync(
                request.OrganizationId,
                provider,
                cancellationToken);

            responseIntegrations.Add(IntegrationResponseFactory.From(
                request.OrganizationId,
                provider,
                integration,
                keys,
                availableModels: availableModels));
        }

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
            new("generator", "Content Generator", generatorChecklist)
        };

        var checklist = singleAnalyzerChecklist.Concat(generatorChecklist).ToList();

        var overview = new OrganizationIntegrationsOverviewResponse(
            request.OrganizationId,
            checklist,
            checklistGroups,
            responseIntegrations);

        return Result.Success(overview);
    }

    private async Task<IReadOnlyList<string>> ResolveAvailableModelsAsync(
        Guid organizationId,
        IntegrationProvider provider,
        CancellationToken cancellationToken)
    {
        if (_models is null || provider.Category != IntegrationProviderCategory.AiModel)
        {
            return provider.AvailableModels;
        }

        var synced = await _models.ListByProviderAsync(
            organizationId,
            provider.Id.Value,
            cancellationToken);

        return synced.Count == 0
            ? provider.AvailableModels
            : synced.Select(x => x.ModelId).ToList();
    }
}
