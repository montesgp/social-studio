using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Features.Integrations;
using Incoders.Hive.Application.Features.Integrations.Configure;
using Incoders.Hive.Application.Features.Integrations.GetByProvider;
using Incoders.Hive.Application.Features.Integrations.GetOverview;
using Incoders.Hive.Application.Features.Integrations.SetDefaultAi;
using Incoders.Hive.Application.Features.Integrations.TestConnection;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Integrations;

public sealed class OrganizationIntegrationsFlowTests
{
    private static readonly DateTime Now = new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Overview_IncludesReadonlySystemDefaultApify_AndChecklistStartsPending()
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

        Assert.All(overview.Value.Checklist, item => Assert.False(item.IsCompleted));
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
            var integration = overview.Value.Integrations.SingleOrDefault(x => x.ProviderKey == providerKey);

            Assert.NotNull(integration);
            Assert.False(string.IsNullOrWhiteSpace(integration!.IconKey));
        }
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
    public async Task ConfigureRequiredProviders_ThenChecklistIsCompleted_AndDefaultAiCanBeSelected()
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

        var canvaConfigured = await harness.Configure.Handle(
            new ConfigureOrganizationIntegrationCommand(
                organizationId,
                "canva",
                IntegrationConnectionMode.BringYourOwnKey,
                true,
                null,
                new Dictionary<string, string>
                {
                    ["access_token"] = "canva-token"
                }),
            CancellationToken.None);

        var blotatoConfigured = await harness.Configure.Handle(
            new ConfigureOrganizationIntegrationCommand(
                organizationId,
                "blotato",
                IntegrationConnectionMode.BringYourOwnKey,
                true,
                null,
                new Dictionary<string, string>
                {
                    ["api_key"] = "blotato-token"
                }),
            CancellationToken.None);

        Assert.True(openAiConfigured.IsSuccess);
        Assert.True(canvaConfigured.IsSuccess);
        Assert.True(blotatoConfigured.IsSuccess);
        Assert.Equal(IntegrationStatus.Configured, openAiConfigured.Value.Status);

        Assert.True((await harness.TestConnection.Handle(
            new TestOrganizationIntegrationConnectionCommand(organizationId, "openai"),
            CancellationToken.None)).IsSuccess);
        Assert.True((await harness.TestConnection.Handle(
            new TestOrganizationIntegrationConnectionCommand(organizationId, "canva"),
            CancellationToken.None)).IsSuccess);
        Assert.True((await harness.TestConnection.Handle(
            new TestOrganizationIntegrationConnectionCommand(organizationId, "blotato"),
            CancellationToken.None)).IsSuccess);

        var defaultAi = await harness.SetDefaultAi.Handle(
            new SetDefaultAiIntegrationCommand(organizationId, "openai", "gpt-4.1-mini"),
            CancellationToken.None);

        Assert.True(defaultAi.IsSuccess);
        Assert.True(defaultAi.Value.IsDefaultAi);

        var overview = await harness.GetOverview.Handle(
            new GetOrganizationIntegrationsQuery(organizationId),
            CancellationToken.None);

        Assert.True(overview.IsSuccess);
        Assert.All(overview.Value.Checklist, item => Assert.True(item.IsCompleted));
        Assert.Equal("openai", overview.Value.DefaultAiProviderKey);
        Assert.Equal("gpt-4.1-mini", overview.Value.DefaultAiModel);
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

            SetDefaultAi = new SetDefaultAiIntegrationHandler(
                _providers,
                Integrations,
                Credentials,
                unitOfWork,
                clock);

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

        public SetDefaultAiIntegrationHandler SetDefaultAi { get; }

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
                CreateProvider(
                    "openai",
                    "OpenAI",
                    IntegrationProviderCategory.AiModel,
                    "openai",
                    true,
                    true,
                    false,
                    false,
                    [new IntegrationCredentialField("api_key", "API key", true, true)],
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
                    [new IntegrationCredentialField("access_token", "Access token", true, true)],
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
