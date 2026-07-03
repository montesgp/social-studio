using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Features.Integrations.GetOverview;
using Incoders.Hive.Application.Features.Integrations.Models;
using Incoders.Hive.Application.Features.Integrations.WorkflowConfigurations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Integrations;

public sealed class ConfigurableWorkflowIntegrationsTests
{
    private static readonly DateTime Now = new(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SyncOrganizationAiModels_PersistsProviderModelsAndOverviewUsesSyncedCatalog()
    {
        var organizationId = Guid.NewGuid();
        var provider = Provider("claude", IntegrationProviderCategory.AiModel, ["stale-hardcoded-model"]);
        var integration = ConnectedIntegration(organizationId, provider.Id, selectedModel: null);
        var providers = new ProviderCatalog(provider);
        var integrations = new IntegrationStore(integration);
        var modelStore = new AiModelStore();
        var catalogClient = new StaticAiModelCatalogClient(
            [
                new AiProviderModel(
                    "claude-live-sonnet",
                    "Claude Live Sonnet",
                    new Dictionary<string, string> { ["source"] = "anthropic" }),
                new AiProviderModel(
                    "claude-live-opus",
                    "Claude Live Opus",
                    new Dictionary<string, string>())
            ]);

        var handler = new SyncOrganizationAiModelsHandler(
            providers,
            integrations,
            new StaticIntegrationResolver(provider.Key),
            new StaticExternalClientFactory(catalogClient),
            modelStore,
            new UnitOfWorkSpy(),
            new TestClock(Now));

        var sync = await handler.Handle(
            new SyncOrganizationAiModelsCommand(organizationId, provider.Key),
            CancellationToken.None);

        Assert.True(sync.IsSuccess);
        Assert.Equal(["claude-live-opus", "claude-live-sonnet"], sync.Value.Models.Select(x => x.ModelId).Order());
        Assert.All(sync.Value.Models, model => Assert.Equal(organizationId, model.OrganizationId));

        var overviewHandler = new GetOrganizationIntegrationsHandler(
            providers,
            integrations,
            new EmptyCredentialStore(),
            modelStore);

        var overview = await overviewHandler.Handle(
            new GetOrganizationIntegrationsQuery(organizationId),
            CancellationToken.None);

        Assert.True(overview.IsSuccess);
        var claude = overview.Value.Integrations.Single(x => x.ProviderKey == "claude");
        Assert.Equal(["claude-live-opus", "claude-live-sonnet"], claude.AvailableModels.Order());
        Assert.DoesNotContain("stale-hardcoded-model", claude.AvailableModels);
    }

    [Fact]
    public async Task SyncOrganizationAiModels_AllowsMediaGenerationProvider_AndMediaCardUsesSyncedCheckpoints()
    {
        var organizationId = Guid.NewGuid();
        var provider = Provider(
            "comfyui",
            IntegrationProviderCategory.MediaGeneration,
            ["v1-5-pruned-emaonly.safetensors"]);
        var integration = ConnectedIntegration(organizationId, provider.Id, selectedModel: null);
        var providers = new ProviderCatalog(provider);
        var integrations = new IntegrationStore(integration);
        var modelStore = new AiModelStore();
        var catalogClient = new StaticAiModelCatalogClient(
            [
                new AiProviderModel(
                    "realisticVisionV60B1_v51HyperVAE.safetensors",
                    "realisticVisionV60B1_v51HyperVAE.safetensors",
                    new Dictionary<string, string> { ["source"] = "checkpoint" })
            ]);

        var handler = new SyncOrganizationAiModelsHandler(
            providers,
            integrations,
            new StaticIntegrationResolver(provider.Key),
            new StaticExternalClientFactory(catalogClient),
            modelStore,
            new UnitOfWorkSpy(),
            new TestClock(Now));

        var sync = await handler.Handle(
            new SyncOrganizationAiModelsCommand(organizationId, provider.Key),
            CancellationToken.None);

        Assert.True(sync.IsSuccess);
        Assert.Equal(
            ["realisticVisionV60B1_v51HyperVAE.safetensors"],
            sync.Value.Models.Select(x => x.ModelId));

        var overviewHandler = new GetOrganizationIntegrationsHandler(
            providers,
            integrations,
            new EmptyCredentialStore(),
            modelStore,
            new StaticExternalClientFactory(catalogClient));

        var overview = await overviewHandler.Handle(
            new GetOrganizationIntegrationsQuery(organizationId),
            CancellationToken.None);

        Assert.True(overview.IsSuccess);
        var comfy = overview.Value.Integrations.Single(x => x.ProviderKey == "comfyui");
        Assert.Equal(["realisticVisionV60B1_v51HyperVAE.safetensors"], comfy.AvailableModels);
        Assert.DoesNotContain("v1-5-pruned-emaonly.safetensors", comfy.AvailableModels);
        // The Media-generation card (ComfyUI's primary category) must advertise model sync so the UI
        // renders the "Sync models" affordance.
        Assert.True(comfy.SupportsModelSync);
    }

    [Fact]
    public async Task SyncOrganizationAiModels_RejectsProviderWithoutCatalogClient()
    {
        var organizationId = Guid.NewGuid();
        var provider = Provider("canva", IntegrationProviderCategory.Design, []);
        var handler = new SyncOrganizationAiModelsHandler(
            new ProviderCatalog(provider),
            new IntegrationStore(ConnectedIntegration(organizationId, provider.Id, selectedModel: null)),
            new StaticIntegrationResolver(provider.Key),
            new StaticExternalClientFactory(client: null, supports: false),
            new AiModelStore(),
            new UnitOfWorkSpy(),
            new TestClock(Now));

        var result = await handler.Handle(
            new SyncOrganizationAiModelsCommand(organizationId, provider.Key),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ExternalProviderUnsupported.Code, result.Error.Code);
    }

    [Fact]
    public async Task WorkflowConfigurationResolver_ResolvesOnlyCurrentOrganizationAndOrdersActorFallbacks()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var apify = Provider("apify", IntegrationProviderCategory.DataExtraction, []);
        var claude = Provider("claude", IntegrationProviderCategory.AiModel, ["claude-live-sonnet"]);
        var providers = new ProviderCatalog(apify, claude);
        var integrationA = ConnectedIntegration(organizationA, apify.Id, selectedModel: null);
        var integrationB = ConnectedIntegration(organizationB, apify.Id, selectedModel: null);
        var integrations = new IntegrationStore(integrationA, integrationB);
        var routes = new WorkflowStepStore();

        var upsert = new UpsertWorkflowConfigurationsHandler(
            providers,
            integrations,
            routes,
            new UnitOfWorkSpy(),
            new TestClock(Now));

        var savedA = await upsert.Handle(
            new UpsertWorkflowConfigurationsCommand(
                organizationA,
                "single-analyzer",
                [
                    new WorkflowStepConfigurationInput(
                        "content-analysis",
                        "scrape-video",
                        "scrape-video",
                        "instagram",
                        "apify",
                        ModelId: null,
                        ActorId: "actor-fallback",
                        Priority: 2,
                        IsEnabled: true),
                    new WorkflowStepConfigurationInput(
                        "content-analysis",
                        "scrape-video",
                        "scrape-video",
                        "instagram",
                        "apify",
                        ModelId: null,
                        ActorId: "actor-primary",
                        Priority: 1,
                        IsEnabled: true)
                ]),
            CancellationToken.None);

        Assert.True(savedA.IsSuccess);

        var savedB = await upsert.Handle(
            new UpsertWorkflowConfigurationsCommand(
                organizationB,
                "single-analyzer",
                [
                    new WorkflowStepConfigurationInput(
                        "content-analysis",
                        "scrape-video",
                        "scrape-video",
                        "instagram",
                        "apify",
                        ModelId: null,
                        ActorId: "other-org-actor",
                        Priority: 1,
                        IsEnabled: true)
                ]),
            CancellationToken.None);

        Assert.True(savedB.IsSuccess);

        var resolver = new WorkflowConfigurationResolver(routes, integrations);
        var resolved = await resolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                organizationA,
                "single-analyzer",
                "content-analysis",
                "scrape-video",
                "scrape-video",
                "instagram"),
            CancellationToken.None);

        Assert.True(resolved.IsSuccess);
        Assert.Equal(["actor-primary", "actor-fallback"], resolved.Value.Select(x => x.ActorId));
        Assert.DoesNotContain(resolved.Value, route => route.ActorId == "other-org-actor");
        Assert.All(resolved.Value, route => Assert.Equal(integrationA.Id.Value, route.OrganizationIntegrationId));
    }

    [Fact]
    public async Task UpsertWorkflowConfiguration_RejectsAiStepWithoutModel()
    {
        var organizationId = Guid.NewGuid();
        var claude = Provider("claude", IntegrationProviderCategory.AiModel, ["claude-live-sonnet"]);
        var upsert = new UpsertWorkflowConfigurationsHandler(
            new ProviderCatalog(claude),
            new IntegrationStore(ConnectedIntegration(organizationId, claude.Id, selectedModel: null)),
            new WorkflowStepStore(),
            new UnitOfWorkSpy(),
            new TestClock(Now));

        var result = await upsert.Handle(
            new UpsertWorkflowConfigurationsCommand(
                organizationId,
                "single-analyzer",
                [
                    new WorkflowStepConfigurationInput(
                        "content-analysis",
                        "analyze-content",
                        "ai-analysis",
                        NetworkKey: null,
                        ProviderKey: "claude",
                        ModelId: null,
                        ActorId: null,
                        Priority: 1,
                        IsEnabled: true)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.AiModelRequired.Code, result.Error.Code);
    }

    [Fact]
    public async Task UpsertWorkflowConfiguration_RejectsProviderOutsideSupportedCatalog()
    {
        var organizationId = Guid.NewGuid();
        var claude = Provider("claude", IntegrationProviderCategory.AiModel, ["claude-live-sonnet"]);
        var handler = new UpsertWorkflowConfigurationsHandler(
            new ProviderCatalog(claude),
            new IntegrationStore(ConnectedIntegration(organizationId, claude.Id, selectedModel: null)),
            new WorkflowStepStore(),
            new UnitOfWorkSpy(),
            new TestClock(Now));

        var result = await handler.Handle(
            new UpsertWorkflowConfigurationsCommand(
                organizationId,
                "single-analyzer",
                [
                    new WorkflowStepConfigurationInput(
                        "content-analysis",
                        "scrape-video",
                        "scrape-video",
                        "instagram",
                        "claude",
                        ModelId: "claude-live-sonnet",
                        ActorId: null,
                        Priority: 1,
                        IsEnabled: true)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderRouteNotConfigured.Code, result.Error.Code);
    }

    [Fact]
    public async Task GetWorkflowConfigurations_ReturnsSelectorCatalog()
    {
        var organizationId = Guid.NewGuid();
        var handler = new GetWorkflowConfigurationsHandler(new WorkflowStepStore());

        var result = await handler.Handle(
            new GetWorkflowConfigurationsQuery(organizationId, "single-analyzer"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var workflow = Assert.Single(result.Value.Catalog.Workflows, x => x.Key == "single-analyzer");
        var profileAnalysis = Assert.Single(workflow.AnalysisTypes, x => x.Key == "profile-analysis");
        var scrapeProfile = Assert.Single(profileAnalysis.Steps, x => x.Key == "scrape-profile");
        Assert.Equal("profile", scrapeProfile.ActorVariantKey);
        Assert.Contains(IntegrationProviderCategory.DataExtraction, scrapeProfile.AllowedProviderCategories);
        Assert.Equal(["apify"], scrapeProfile.AllowedProviderKeys);
    }

    private static IntegrationProvider Provider(
        string key,
        IntegrationProviderCategory category,
        IReadOnlyList<string> availableModels)
    {
        var created = IntegrationProvider.Create(
            IntegrationProviderId.New(),
            key,
            key,
            category,
            description: null,
            iconKey: key,
            supportsBringYourOwnKey: true,
            supportsManagedByIncoders: false,
            supportsSystemDefault: false,
            systemDefaultReadOnly: false,
            [new IntegrationCredentialField("api_key", "API key", true, true)],
            availableModels,
            Now);

        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private static OrganizationIntegration ConnectedIntegration(
        Guid organizationId,
        IntegrationProviderId providerId,
        string? selectedModel)
    {
        var created = OrganizationIntegration.Create(
            OrganizationIntegrationId.New(),
            organizationId,
            providerId,
            IntegrationConnectionMode.BringYourOwnKey,
            IntegrationStatus.Connected,
            isReadOnly: false,
            selectedModel,
            lastTestedAtUtc: Now,
            lastError: null,
            Now);

        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private sealed class ProviderCatalog : IIntegrationProviderCatalogRepository
    {
        private readonly Dictionary<string, IntegrationProvider> _byKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<IntegrationProviderId, IntegrationProvider> _byId = new();

        public ProviderCatalog(params IntegrationProvider[] providers)
        {
            foreach (var provider in providers)
            {
                _byKey[provider.Key] = provider;
                _byId[provider.Id] = provider;
            }
        }

        public Task<IReadOnlyList<IntegrationProvider>> ListActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IntegrationProvider>>(_byKey.Values.ToList());

        public Task<IntegrationProvider?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            _byKey.TryGetValue(key, out var provider);
            return Task.FromResult(provider);
        }

        public Task<IntegrationProvider?> GetByIdAsync(IntegrationProviderId id, CancellationToken cancellationToken = default)
        {
            _byId.TryGetValue(id, out var provider);
            return Task.FromResult(provider);
        }

        public Task UpsertAsync(IntegrationProvider provider, CancellationToken cancellationToken = default)
        {
            _byKey[provider.Key] = provider;
            _byId[provider.Id] = provider;
            return Task.CompletedTask;
        }
    }

    private sealed class IntegrationStore : IOrganizationIntegrationRepository
    {
        private readonly List<OrganizationIntegration> _items;

        public IntegrationStore(params OrganizationIntegration[] items) => _items = [.. items];

        public Task<OrganizationIntegration?> GetByOrganizationAndProviderAsync(
            Guid organizationId,
            IntegrationProviderId providerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.FirstOrDefault(x => x.OrganizationId == organizationId && x.ProviderId == providerId));

        public Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationIntegration>>(
                _items.Where(x => x.OrganizationId == organizationId).ToList());

        public Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAndCategoryAsync(
            Guid organizationId,
            IntegrationProviderCategory category,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationIntegration>>(
                _items.Where(x => x.OrganizationId == organizationId).ToList());

        public Task AddAsync(OrganizationIntegration integration, CancellationToken cancellationToken = default)
        {
            _items.Add(integration);
            return Task.CompletedTask;
        }

        public void Update(OrganizationIntegration integration) => _ = integration;

        public void Remove(OrganizationIntegration integration) => _items.Remove(integration);
    }

    private sealed class EmptyCredentialStore : IOrganizationIntegrationCredentialRepository
    {
        public Task<IReadOnlyList<OrganizationIntegrationCredential>> ListByIntegrationIdAsync(
            OrganizationIntegrationId integrationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationIntegrationCredential>>([]);

        public Task UpsertAsync(OrganizationIntegrationCredential credential, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveByIntegrationIdAsync(
            OrganizationIntegrationId integrationId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class AiModelStore : IOrganizationAiModelRepository
    {
        private readonly List<OrganizationAiModelRecord> _items = [];

        public Task<IReadOnlyList<OrganizationAiModelRecord>> ListByProviderAsync(
            Guid organizationId,
            Guid providerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationAiModelRecord>>(
                _items
                    .Where(x => x.OrganizationId == organizationId && x.ProviderId == providerId && x.IsAvailable)
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        public Task UpsertManyAsync(
            IReadOnlyList<OrganizationAiModelRecord> models,
            CancellationToken cancellationToken = default)
        {
            foreach (var model in models)
            {
                _items.RemoveAll(x =>
                    x.OrganizationId == model.OrganizationId
                    && x.ProviderId == model.ProviderId
                    && string.Equals(x.ModelId, model.ModelId, StringComparison.OrdinalIgnoreCase));
                _items.Add(model);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class WorkflowStepStore : IWorkflowStepConfigurationRepository
    {
        private readonly List<WorkflowStepConfigurationRecord> _items = [];

        public Task<IReadOnlyList<WorkflowStepConfigurationRecord>> ListAsync(
            Guid organizationId,
            string? workflowKey = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowStepConfigurationRecord>>(
                _items
                    .Where(x => x.OrganizationId == organizationId)
                    .Where(x => string.IsNullOrWhiteSpace(workflowKey)
                        || string.Equals(x.WorkflowKey, workflowKey, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Priority)
                    .ToList());

        public Task<IReadOnlyList<WorkflowStepConfigurationRecord>> ListForStepAsync(
            WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowStepConfigurationRecord>>(
                _items
                    .Where(x => x.OrganizationId == request.OrganizationId)
                    .Where(x => string.Equals(x.WorkflowKey, request.WorkflowKey, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.Equals(x.AnalysisType, request.AnalysisType, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.Equals(x.StepKey, request.StepKey, StringComparison.OrdinalIgnoreCase))
                    .Where(x => request.OperationKey is null
                        || x.OperationKey is null
                        || string.Equals(x.OperationKey, request.OperationKey, StringComparison.OrdinalIgnoreCase))
                    .Where(x => request.NetworkKey is null
                        || x.NetworkKey is null
                        || string.Equals(x.NetworkKey, request.NetworkKey, StringComparison.OrdinalIgnoreCase))
                    .Where(x => x.IsEnabled)
                    .OrderBy(x => x.Priority)
                    .ToList());

        public Task ReplaceWorkflowAsync(
            Guid organizationId,
            string workflowKey,
            IReadOnlyList<WorkflowStepConfigurationRecord> rows,
            CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(x =>
                x.OrganizationId == organizationId
                && string.Equals(x.WorkflowKey, workflowKey, StringComparison.OrdinalIgnoreCase));
            _items.AddRange(rows);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticIntegrationResolver : IIntegrationResolver
    {
        private readonly string _providerKey;

        public StaticIntegrationResolver(string providerKey) => _providerKey = providerKey;

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId,
            string providerKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new ResolvedIntegration(
                organizationId,
                _providerKey,
                _providerKey,
                IntegrationProviderCategory.AiModel,
                IntegrationConnectionMode.BringYourOwnKey,
                IntegrationStatus.Connected,
                IsReadOnly: false,
                SelectedAiModel: null,
                new Dictionary<string, string> { ["api_key"] = "test-key" })));
    }

    private sealed class StaticExternalClientFactory : IExternalProviderClientFactory
    {
        private readonly IAiModelCatalogClient? _client;
        private readonly bool _supports;

        public StaticExternalClientFactory(IAiModelCatalogClient? client, bool supports = true)
        {
            _client = client;
            _supports = supports;
        }

        public TClient Create<TClient>(ResolvedProviderCredential credential)
            where TClient : notnull =>
            _client is TClient typed
                ? typed
                : throw new InvalidOperationException($"No client for {typeof(TClient).Name}.");

        public bool Supports<TClient>(string providerKey)
            where TClient : notnull => _supports;
    }

    private sealed class StaticAiModelCatalogClient : IAiModelCatalogClient
    {
        private readonly IReadOnlyList<AiProviderModel> _models;

        public StaticAiModelCatalogClient(IReadOnlyList<AiProviderModel> models) => _models = models;

        public Task<Result<IReadOnlyList<AiProviderModel>>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(_models));
    }

    private sealed class UnitOfWorkSpy : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
