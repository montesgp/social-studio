using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
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
    private readonly IExternalProviderClientFactory? _clientFactory;
    private readonly INicheSearchConfigurationProbe? _nicheSearchProbe;
    private readonly INicheDiscoveryOriginProbe? _nicheDiscoveryOriginProbe;

    public GetOrganizationIntegrationsHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IOrganizationIntegrationCredentialRepository credentials,
        IOrganizationAiModelRepository? models = null,
        IExternalProviderClientFactory? clientFactory = null,
        INicheSearchConfigurationProbe? nicheSearchProbe = null,
        INicheDiscoveryOriginProbe? nicheDiscoveryOriginProbe = null)
    {
        _providers = providers;
        _integrations = integrations;
        _credentials = credentials;
        _models = models;
        _clientFactory = clientFactory;
        _nicheSearchProbe = nicheSearchProbe;
        _nicheDiscoveryOriginProbe = nicheDiscoveryOriginProbe;
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

            // A single connector (one credential) is presented once per category it serves —
            // e.g. OpenAI under both AI model providers and Media generation providers — each card
            // carrying only the models relevant to that category.
            foreach (var category in provider.Categories)
            {
                var availableModels = await ResolveAvailableModelsAsync(
                    request.OrganizationId,
                    provider,
                    category,
                    cancellationToken);

                // The synced catalog is keyed by provider and maps to its PRIMARY category, so only
                // that card advertises "Sync models" — and only when a catalog client actually exists.
                var supportsModelSync = category == provider.Category
                    && (_clientFactory?.Supports<IAiModelCatalogClient>(provider.Key) ?? false);

                responseIntegrations.Add(IntegrationResponseFactory.From(
                    request.OrganizationId,
                    provider,
                    integration,
                    keys,
                    availableModels: availableModels,
                    category: category,
                    supportsModelSync: supportsModelSync));
            }
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

        singleAnalyzerChecklist.AddRange(
            await BuildNicheSearchChecklistItemsAsync(request.OrganizationId, cancellationToken));

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

        var nicheDiscoveryChecklist = await BuildNicheDiscoveryChecklistItemsAsync(request.OrganizationId, cancellationToken);

        var checklistGroups = new List<IntegrationSetupChecklistResponse>
        {
            new("single-analyzer", "Single Analyzer", singleAnalyzerChecklist),
            new("generator", "Content Generator", generatorChecklist),
            new("niche-discovery", "Niche Discovery", nicheDiscoveryChecklist)
        };

        var checklist = singleAnalyzerChecklist.Concat(generatorChecklist).Concat(nicheDiscoveryChecklist).ToList();

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
        IntegrationProviderCategory category,
        CancellationToken cancellationToken)
    {
        // A provider's synced catalog is keyed by provider, not category, so it maps to the provider's
        // PRIMARY category card (e.g. OpenAI→AI models, ComfyUI→Media generation). Secondary-category
        // cards (e.g. OpenAI under Media generation) keep the static catalog menu so text models never
        // leak into the image-model selector.
        if (_models is null || category != provider.Category)
        {
            return provider.ModelsFor(category);
        }

        var synced = await _models.ListByProviderAsync(
            organizationId,
            provider.Id.Value,
            cancellationToken);

        return synced.Count == 0
            ? provider.ModelsFor(category)
            : synced.Select(x => x.ModelId).ToList();
    }

    /// <summary>
    /// Emits ONE optional checklist item per network that actually has a working niche-search adapter
    /// — never a single hardcoded "Configure a TikTok niche-search actor" line regardless of the network
    /// the operator is working with.
    ///
    /// SHAPE RATIONALE (per-network items rather than one network-parameterised item):
    /// the readiness answer is genuinely per network — TikTok can be configured while Instagram cannot
    /// be configured AT ALL — so one boolean cannot carry it, and a query parameter on the overview
    /// would make a cacheable read-only response network-dependent for every other consumer. Per-network
    /// items keep the existing response shape and let each caller pick the network it cares about.
    ///
    /// ABSENCE IS MEANINGFUL: a network with no adapter (Instagram, YouTube today) gets NO item, and
    /// callers must read that as "not supported yet". Emitting a "configure an actor" item for a network
    /// whose runtime path degrades straight to Unconfigured would send the operator to buy and wire an
    /// actor that can never be dispatched — worse than saying nothing.
    /// </summary>
    private async Task<IReadOnlyList<IntegrationSetupChecklistItemResponse>> BuildNicheSearchChecklistItemsAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (_nicheSearchProbe is null)
        {
            return [];
        }

        var items = new List<IntegrationSetupChecklistItemResponse>();

        foreach (var network in _nicheSearchProbe.SupportedNetworks)
        {
            // Optional (INT-DISC-03): discovery still works on references/own with the niche stage
            // unconfigured. This item is the operator's pointer to the discover-niche-content step,
            // not a gate (D11/D12/CD-12).
            var resolves = await _nicheSearchProbe.ResolvesActorAsync(organizationId, network.NetworkKey, cancellationToken);

            items.Add(new IntegrationSetupChecklistItemResponse(
                $"{NicheSearchChecklistItemKeyPrefix}{network.NetworkKey}",
                $"Configure a {network.DisplayName} niche-search actor",
                false,
                resolves,
                $"OPTIONAL. Map or route a {network.DisplayName} niche-search actor to turn on content " +
                "discovery for this network. Discovery still works from reference/own accounts without it."));
        }

        return items;
    }

    /// <summary>
    /// Checklist key prefix for the per-network niche-search readiness items; the network key is
    /// appended verbatim (e.g. <c>single-analyzer-niche-search-actor-tiktok</c>).
    /// </summary>
    public const string NicheSearchChecklistItemKeyPrefix = "single-analyzer-niche-search-actor-";

    /// <summary>
    /// WU-4 (N12, INT-ND-03): the "niche-discovery" checklist group — a hard deliverable, not a
    /// nicety. A workflow key with no matching checklist group renders "not configurable yet" in
    /// Settings even when the backend is fully wired (`WorkflowConfigurationCatalog.cs` trap).
    /// Backed by <see cref="INicheDiscoveryOriginProbe"/>, real implementation lands in WU-5 —
    /// until then, both items report unsatisfied (probe not wired / no route resolves).
    /// </summary>
    private async Task<IReadOnlyList<IntegrationSetupChecklistItemResponse>> BuildNicheDiscoveryChecklistItemsAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var isReady = _nicheDiscoveryOriginProbe is not null
            && await _nicheDiscoveryOriginProbe.IsReadyAsync(organizationId, "youtube", cancellationToken);

        return
        [
            new(
                "niche-discovery-api-key",
                "Connect a YouTube Data API key",
                true,
                isReady,
                "Connect an official YouTube Data API v3 key to enable channel/video search for niche discovery."),
            new(
                "niche-discovery-origin",
                "Enable a discovery origin for channel search",
                true,
                isReady,
                "At least one discovery origin route must resolve for the channel search stage before a run can start.")
        ];
    }
}
