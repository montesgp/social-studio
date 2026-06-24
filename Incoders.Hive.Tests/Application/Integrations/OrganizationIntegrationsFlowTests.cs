using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Features.Integrations;
using Incoders.Hive.Application.Features.Integrations.Configure;
using Incoders.Hive.Application.Features.Integrations.GetByProvider;
using Incoders.Hive.Application.Features.Integrations.GetOverview;
using Incoders.Hive.Application.Features.Integrations.TestConnection;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Integrations;

public sealed class OrganizationIntegrationsFlowTests
{
    private static readonly DateTime Now = new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Overview_IncludesReadonlySystemDefaultApify_AndChecklistRequiresAiOnly()
    {
        var harness = new IntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        var overview = await harness.GetOverview.Handle(
            new GetOrganizationIntegrationsQuery(organizationId),
            CancellationToken.None);

        Assert.True(overview.IsSuccess);

        var apify = overview.Value.Integrations.Single(x => x.ProviderKey == "apify");
        Assert.Equal(IntegrationConnectionMode.SystemDefault, apify.ConnectionMode);
        Assert.Equal(IntegrationStatus.Connected, apify.Status);
        Assert.True(apify.IsReadOnly);
        Assert.Null(apify.IntegrationId);

        Assert.Equal(["single-analyzer", "generator"], overview.Value.ChecklistGroups.Select(x => x.Key));
        Assert.All(
            overview.Value.ChecklistGroups,
            group =>
            {
                Assert.Contains(group.Items, item => item.Key.EndsWith("data-provider", StringComparison.Ordinal) && item.IsCompleted);
                Assert.Contains(group.Items, item => item.Key.EndsWith("ai-provider", StringComparison.Ordinal) && !item.IsCompleted);
            });
    }

    [Fact]
    public async Task Overview_ExposesCurrentProviderCatalogWithIconKeys()
    {
        var harness = new IntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        var overview = await harness.GetOverview.Handle(
            new GetOrganizationIntegrationsQuery(organizationId),
            CancellationToken.None);

        Assert.True(overview.IsSuccess);

        var expectedProviders = new[]
        {
            "google-drive",
            "apify",
            "data365",
            "claude",
            "openai",
            "genspark-ai",
            "tiktok",
            "youtube",
            "instagram"
        };

        foreach (var providerKey in expectedProviders)
        {
            var integration = overview.Value.Integrations.FirstOrDefault(x => x.ProviderKey == providerKey);

            Assert.NotNull(integration);
            Assert.False(string.IsNullOrWhiteSpace(integration!.IconKey));
        }
    }

    [Fact]
    public async Task Overview_ExplodesMultiCategoryProvider_IntoOneCardPerCategoryWithItsModels()
    {
        var harness = new IntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        var overview = await harness.GetOverview.Handle(
            new GetOrganizationIntegrationsQuery(organizationId),
            CancellationToken.None);

        Assert.True(overview.IsSuccess);

        var openAiCards = overview.Value.Integrations
            .Where(x => x.ProviderKey == "openai")
            .ToList();

        // One shared OpenAI connector, presented under both sections.
        Assert.Equal(2, openAiCards.Count);

        var aiModelCard = openAiCards.Single(x => x.Category == IntegrationProviderCategory.AiModel);
        var mediaCard = openAiCards.Single(x => x.Category == IntegrationProviderCategory.MediaGeneration);

        // Each card exposes ONLY the models for its own category.
        Assert.Equal(["gpt-4.1", "gpt-4o"], aiModelCard.AvailableModels);
        Assert.Equal(["dall-e-3", "gpt-image-1"], mediaCard.AvailableModels);
    }

    [Fact]
    public async Task Overview_ConnectedMultiCategoryProvider_KeepsDistinctCategoriesPerCard()
    {
        var harness = new IntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        // Connect the shared OpenAI credential once.
        var configured = await harness.Configure.Handle(
            new ConfigureOrganizationIntegrationCommand(
                organizationId,
                "openai",
                IntegrationConnectionMode.BringYourOwnKey,
                true,
                "gpt-4.1",
                new Dictionary<string, string>
                {
                    ["api_key"] = "sk-test-openai"
                }),
            CancellationToken.None);
        Assert.True(configured.IsSuccess);

        var overview = await harness.GetOverview.Handle(
            new GetOrganizationIntegrationsQuery(organizationId),
            CancellationToken.None);

        Assert.True(overview.IsSuccess);

        var openAiCards = overview.Value.Integrations
            .Where(x => x.ProviderKey == "openai")
            .ToList();

        // Even once connected, the card MUST stay split across both sections — not collapse to AiModel.
        Assert.Equal(2, openAiCards.Count);
        Assert.Contains(openAiCards, x => x.Category == IntegrationProviderCategory.AiModel);
        Assert.Contains(openAiCards, x => x.Category == IntegrationProviderCategory.MediaGeneration);

        // Both cards reference the same shared integration (one credential).
        Assert.Single(openAiCards.Select(x => x.IntegrationId).Distinct());

        var mediaCard = openAiCards.Single(x => x.Category == IntegrationProviderCategory.MediaGeneration);
        Assert.Equal(["dall-e-3", "gpt-image-1"], mediaCard.AvailableModels);
    }

    [Fact]
    public async Task GetByProvider_ReturnsSingleOrganizationIntegration()
    {
        var harness = new IntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        var result = await harness.GetByProvider.Handle(
            new GetOrganizationIntegrationByProviderQuery(organizationId, "google-drive"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("google-drive", result.Value.ProviderKey);
        Assert.Equal("google-drive", result.Value.IconKey);
    }

    [Fact]
    public async Task ConfigureRequiredProviders_ThenChecklistGroupsAreCompleted()
    {
        var harness = new IntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        var openAiConfigured = await harness.Configure.Handle(
            new ConfigureOrganizationIntegrationCommand(
                organizationId,
                "openai",
                IntegrationConnectionMode.BringYourOwnKey,
                true,
                "gpt-4.1-mini",
                new Dictionary<string, string>
                {
                    ["api_key"] = "sk-test-openai"
                }),
            CancellationToken.None);

        Assert.True(openAiConfigured.IsSuccess);
        Assert.Equal(IntegrationStatus.Configured, openAiConfigured.Value.Status);

        Assert.True((await harness.TestConnection.Handle(
            new TestOrganizationIntegrationConnectionCommand(organizationId, "openai"),
            CancellationToken.None)).IsSuccess);

        var overview = await harness.GetOverview.Handle(
            new GetOrganizationIntegrationsQuery(organizationId),
            CancellationToken.None);

        Assert.True(overview.IsSuccess);
        Assert.All(overview.Value.ChecklistGroups, group => Assert.All(group.Items, item => Assert.True(item.IsCompleted)));
        Assert.DoesNotContain(overview.Value.Checklist, item => item.Key.Contains("canva", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(overview.Value.Checklist, item => item.Key.Contains("blotato", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConfigureProvider_SavesCredentialsAsConfiguredWithoutMarkingConnected_AndGetByProviderReturnsValues()
    {
        var harness = new IntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        var configured = await harness.Configure.Handle(
            new ConfigureOrganizationIntegrationCommand(
                organizationId,
                "apify",
                IntegrationConnectionMode.BringYourOwnKey,
                true,
                null,
                new Dictionary<string, string>
                {
                    ["api_key"] = "apify-token",
                    ["actor_id"] = "clockworks/tiktok-scraper"
                }),
            CancellationToken.None);

        Assert.True(configured.IsSuccess);
        Assert.Equal(IntegrationStatus.Configured, configured.Value.Status);
        Assert.Empty(configured.Value.CredentialValues);

        var loaded = await harness.GetByProvider.Handle(
            new GetOrganizationIntegrationByProviderQuery(organizationId, "apify"),
            CancellationToken.None);

        Assert.True(loaded.IsSuccess);
        Assert.Equal(IntegrationStatus.Configured, loaded.Value.Status);
        Assert.Equal("apify-token", loaded.Value.CredentialValues["api_key"]);
        Assert.Equal("clockworks/tiktok-scraper", loaded.Value.CredentialValues["actor_id"]);
    }

    [Fact]
    public async Task ConfigureApify_AllowsActorMappingsByNetworkAndVariant()
    {
        var harness = new IntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();
        const string actorMappingsJson = "[{\"networkKey\":\"tiktok\",\"variantKey\":\"single-video\",\"actorId\":\"clockworks/tiktok-scraper\"},{\"networkKey\":\"instagram\",\"variantKey\":\"profile\",\"actorId\":\"vendor/instagram-profile\"}]";

        var configured = await harness.Configure.Handle(
            new ConfigureOrganizationIntegrationCommand(
                organizationId,
                "apify",
                IntegrationConnectionMode.BringYourOwnKey,
                true,
                null,
                new Dictionary<string, string>
                {
                    ["api_key"] = "apify-token",
                    ["actor_mappings_json"] = actorMappingsJson
                }),
            CancellationToken.None);

        Assert.True(configured.IsSuccess);
        Assert.Equal(IntegrationStatus.Configured, configured.Value.Status);

        var loaded = await harness.GetByProvider.Handle(
            new GetOrganizationIntegrationByProviderQuery(organizationId, "apify"),
            CancellationToken.None);

        Assert.True(loaded.IsSuccess);
        Assert.Equal(actorMappingsJson, loaded.Value.CredentialValues["actor_mappings_json"]);
    }

    private sealed class IntegrationsHarness
    {
        private readonly InMemoryIntegrationProviderCatalogRepository _providers;

        public IntegrationsHarness(DateTime nowUtc)
        {
            _providers = new InMemoryIntegrationProviderCatalogRepository(nowUtc);
            Integrations = new InMemoryOrganizationIntegrationRepository(_providers);
            Credentials = new InMemoryOrganizationIntegrationCredentialRepository();

            var protector = new FakeIntegrationSecretProtector();
            var unitOfWork = new InMemoryUnitOfWork();
            var clock = new TestClock(nowUtc);
            var tester = new AlwaysSuccessfulConnectionTester();

            Configure = new ConfigureOrganizationIntegrationHandler(
                _providers,
                Integrations,
                Credentials,
                protector,
                unitOfWork,
                clock);

            GetOverview = new GetOrganizationIntegrationsHandler(
                _providers,
                Integrations,
                Credentials);

            GetByProvider = new GetOrganizationIntegrationByProviderHandler(
                _providers,
                Integrations,
                Credentials,
                protector);

            TestConnection = new TestOrganizationIntegrationConnectionHandler(
                _providers,
                Integrations,
                Credentials,
                protector,
                tester,
                unitOfWork,
                clock);
        }

        public InMemoryOrganizationIntegrationRepository Integrations { get; }

        public InMemoryOrganizationIntegrationCredentialRepository Credentials { get; }

        public ConfigureOrganizationIntegrationHandler Configure { get; }

        public GetOrganizationIntegrationsHandler GetOverview { get; }

        public GetOrganizationIntegrationByProviderHandler GetByProvider { get; }

        public TestOrganizationIntegrationConnectionHandler TestConnection { get; }
    }

    private sealed class InMemoryIntegrationProviderCatalogRepository : IIntegrationProviderCatalogRepository
    {
        private readonly Dictionary<string, IntegrationProvider> _providersByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<IntegrationProviderId, IntegrationProvider> _providersById = new();

        public InMemoryIntegrationProviderCatalogRepository(DateTime nowUtc)
        {
            foreach (var provider in SeedProviders(nowUtc))
            {
                _providersByKey[provider.Key] = provider;
                _providersById[provider.Id] = provider;
            }
        }

        public Task<IReadOnlyList<IntegrationProvider>> ListActiveAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<IntegrationProvider>>(_providersByKey.Values.ToList());
        }

        public Task<IntegrationProvider?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _providersByKey.TryGetValue(key, out var provider);
            return Task.FromResult(provider);
        }

        public Task<IntegrationProvider?> GetByIdAsync(IntegrationProviderId id, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _providersById.TryGetValue(id, out var provider);
            return Task.FromResult(provider);
        }

        public Task UpsertAsync(IntegrationProvider provider, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _providersByKey[provider.Key] = provider;
            _providersById[provider.Id] = provider;
            return Task.CompletedTask;
        }

        public bool TryGetCategory(IntegrationProviderId providerId, out IntegrationProviderCategory category)
        {
            if (_providersById.TryGetValue(providerId, out var provider))
            {
                category = provider.Category;
                return true;
            }

            category = default;
            return false;
        }

        private static IReadOnlyList<IntegrationProvider> SeedProviders(DateTime nowUtc)
        {
            return
            [
                CreateMultiProvider(
                    "openai",
                    "OpenAI",
                    [IntegrationProviderCategory.AiModel, IntegrationProviderCategory.MediaGeneration],
                    "openai",
                    true,
                    true,
                    false,
                    false,
                    [new IntegrationCredentialField("api_key", "API key", true, true)],
                    new Dictionary<IntegrationProviderCategory, IReadOnlyList<string>>
                    {
                        [IntegrationProviderCategory.AiModel] = ["gpt-4.1", "gpt-4o"],
                        [IntegrationProviderCategory.MediaGeneration] = ["dall-e-3", "gpt-image-1"],
                    },
                    nowUtc),
                CreateProvider(
                    "canva",
                    "Canva",
                    IntegrationProviderCategory.Design,
                    "canva",
                    true,
                    false,
                    false,
                    false,
                    [
                        new IntegrationCredentialField("client_id",     "Client ID",     true,  true),
                        new IntegrationCredentialField("client_secret", "Client secret", true,  true),
                        new IntegrationCredentialField("scopes",        "Scopes",        false, false)
                    ],
                    nowUtc),
                CreateProvider(
                    "blotato",
                    "Blotato",
                    IntegrationProviderCategory.Publishing,
                    "blotato",
                    true,
                    false,
                    false,
                    false,
                    [new IntegrationCredentialField("api_key", "API key", true, true)],
                    nowUtc),
                CreateProvider(
                    "apify",
                    "Apify",
                    IntegrationProviderCategory.DataExtraction,
                    "apify",
                    true,
                    true,
                    true,
                    true,
                    [
                        new IntegrationCredentialField("api_key", "API key", true, true),
                        new IntegrationCredentialField("actor_id", "Default actor ID", false, false),
                        new IntegrationCredentialField("actor_mappings_json", "Actor mappings", false, false)
                    ],
                    nowUtc),
                CreateProvider(
                    "google-drive",
                    "Google Drive",
                    IntegrationProviderCategory.Publishing,
                    "google-drive",
                    true,
                    true,
                    false,
                    false,
                    [new IntegrationCredentialField("refresh_token", "Refresh token", true, true)],
                    nowUtc),
                CreateProvider(
                    "data365",
                    "Data365",
                    IntegrationProviderCategory.DataExtraction,
                    "data365",
                    true,
                    true,
                    false,
                    false,
                    [new IntegrationCredentialField("api_key", "API key", true, true)],
                    nowUtc),
                CreateProvider(
                    "claude",
                    "Claude",
                    IntegrationProviderCategory.AiModel,
                    "claude",
                    true,
                    true,
                    false,
                    false,
                    [new IntegrationCredentialField("api_key", "API key", true, true)],
                    nowUtc),
                CreateProvider(
                    "genspark-ai",
                    "GenSpark AI",
                    IntegrationProviderCategory.AiModel,
                    "genspark-ai",
                    true,
                    true,
                    false,
                    false,
                    [new IntegrationCredentialField("api_key", "API key", true, true)],
                    nowUtc),
                CreateProvider(
                    "tiktok",
                    "TikTok",
                    IntegrationProviderCategory.Publishing,
                    "tiktok",
                    true,
                    false,
                    false,
                    false,
                    [new IntegrationCredentialField("access_token", "Access token", true, true)],
                    nowUtc),
                CreateProvider(
                    "youtube",
                    "YouTube",
                    IntegrationProviderCategory.Publishing,
                    "youtube",
                    true,
                    false,
                    false,
                    false,
                    [new IntegrationCredentialField("refresh_token", "Refresh token", true, true)],
                    nowUtc),
                CreateProvider(
                    "instagram",
                    "Instagram",
                    IntegrationProviderCategory.Publishing,
                    "instagram",
                    true,
                    false,
                    false,
                    false,
                    [new IntegrationCredentialField("access_token", "Access token", true, true)],
                    nowUtc)
            ];
        }

        private static IntegrationProvider CreateProvider(
            string key,
            string displayName,
            IntegrationProviderCategory category,
            string iconKey,
            bool supportsByok,
            bool supportsManaged,
            bool supportsSystemDefault,
            bool readonlySystemDefault,
            IReadOnlyList<IntegrationCredentialField> fields,
            DateTime nowUtc)
        {
            var created = IntegrationProvider.Create(
                IntegrationProviderId.New(),
                key,
                displayName,
                category,
                null,
                iconKey,
                supportsByok,
                supportsManaged,
                supportsSystemDefault,
                readonlySystemDefault,
                fields,
                [],
                nowUtc);

            return created.Value;
        }

        private static IntegrationProvider CreateMultiProvider(
            string key,
            string displayName,
            IReadOnlyList<IntegrationProviderCategory> categories,
            string iconKey,
            bool supportsByok,
            bool supportsManaged,
            bool supportsSystemDefault,
            bool readonlySystemDefault,
            IReadOnlyList<IntegrationCredentialField> fields,
            IReadOnlyDictionary<IntegrationProviderCategory, IReadOnlyList<string>> modelsByCategory,
            DateTime nowUtc)
        {
            var created = IntegrationProvider.Create(
                IntegrationProviderId.New(),
                key,
                displayName,
                categories,
                null,
                iconKey,
                supportsByok,
                supportsManaged,
                supportsSystemDefault,
                readonlySystemDefault,
                fields,
                modelsByCategory,
                nowUtc);

            return created.Value;
        }
    }

    private sealed class InMemoryOrganizationIntegrationRepository : IOrganizationIntegrationRepository
    {
        private readonly List<OrganizationIntegration> _integrations = [];
        private readonly InMemoryIntegrationProviderCatalogRepository _providers;

        public InMemoryOrganizationIntegrationRepository(InMemoryIntegrationProviderCatalogRepository providers)
        {
            _providers = providers;
        }

        public Task<OrganizationIntegration?> GetByOrganizationAndProviderAsync(
            Guid organizationId,
            IntegrationProviderId providerId,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            var integration = _integrations.FirstOrDefault(x =>
                x.OrganizationId == organizationId && x.ProviderId == providerId);

            return Task.FromResult(integration);
        }

        public Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<OrganizationIntegration>>(
                _integrations.Where(x => x.OrganizationId == organizationId).ToList());
        }

        public Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAndCategoryAsync(
            Guid organizationId,
            IntegrationProviderCategory category,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;

            var filtered = _integrations
                .Where(x => x.OrganizationId == organizationId)
                .Where(x => _providers.TryGetCategory(x.ProviderId, out var currentCategory) && currentCategory == category)
                .ToList();

            return Task.FromResult<IReadOnlyList<OrganizationIntegration>>(filtered);
        }

        public Task AddAsync(OrganizationIntegration integration, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _integrations.Add(integration);
            return Task.CompletedTask;
        }

        public void Update(OrganizationIntegration integration)
        {
            _ = integration;
        }

        public void Remove(OrganizationIntegration integration)
        {
            _integrations.RemoveAll(x => x.Id == integration.Id);
        }
    }

    private sealed class InMemoryOrganizationIntegrationCredentialRepository : IOrganizationIntegrationCredentialRepository
    {
        private readonly Dictionary<OrganizationIntegrationId, List<OrganizationIntegrationCredential>> _credentials = new();

        public Task<IReadOnlyList<OrganizationIntegrationCredential>> ListByIntegrationIdAsync(
            OrganizationIntegrationId integrationId,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _credentials.TryGetValue(integrationId, out var values);
            return Task.FromResult<IReadOnlyList<OrganizationIntegrationCredential>>(values?.ToList() ?? []);
        }

        public Task UpsertAsync(OrganizationIntegrationCredential credential, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;

            if (!_credentials.TryGetValue(credential.OrganizationIntegrationId, out var values))
            {
                values = [];
                _credentials[credential.OrganizationIntegrationId] = values;
            }

            var index = values.FindIndex(x => string.Equals(x.Key, credential.Key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                values[index] = credential;
            }
            else
            {
                values.Add(credential);
            }

            return Task.CompletedTask;
        }

        public Task RemoveByIntegrationIdAsync(
            OrganizationIntegrationId integrationId,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _credentials.Remove(integrationId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIntegrationSecretProtector : IIntegrationSecretProtector
    {
        public string Protect(string value) => $"enc::{value}";

        public string Unprotect(string protectedValue) => protectedValue.Replace("enc::", string.Empty, StringComparison.Ordinal);
    }

    private sealed class AlwaysSuccessfulConnectionTester : IIntegrationConnectionTester
    {
        public Task<IntegrationConnectionTestResult> TestAsync(
            IntegrationProvider provider,
            IntegrationConnectionMode mode,
            IReadOnlyDictionary<string, string> credentials,
            CancellationToken cancellationToken = default)
        {
            _ = provider;
            _ = mode;
            _ = credentials;
            _ = cancellationToken;
            return Task.FromResult(new IntegrationConnectionTestResult(true));
        }
    }

    private sealed class InMemoryUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(0);
        }
    }
}
