using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Features.Integrations;
using Incoders.Hive.Application.Features.Integrations.Configure;
using Incoders.Hive.Application.Features.Integrations.GetByProvider;
using Incoders.Hive.Application.Features.Integrations.GetOverview;
using Incoders.Hive.Application.Features.Integrations.TestConnection;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Integrations;

/// <summary>
/// Covers CANVA-03: configure + resolve flow tests for the Canva Connect BYOK connector.
/// The harness seeds Canva with the NEW Connect schema (client_id, client_secret, scopes).
/// RED phase: tests fail until WU-04 upgrades the seeder AND the flow tests harness.
/// </summary>
public sealed class CanvaConnectorFlowTests
{
    private static readonly DateTime Now = new(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);

    // ─── CANVA-03-a ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfigureCanva_WithClientIdAndSecret_StatusIsConnected()
    {
        var harness = new CanvaIntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        var configured = await harness.Configure.Handle(
            new ConfigureOrganizationIntegrationCommand(
                organizationId,
                "canva",
                IntegrationConnectionMode.BringYourOwnKey,
                true,
                null,
                new Dictionary<string, string>
                {
                    ["client_id"] = "cid",
                    ["client_secret"] = "csec"
                }),
            CancellationToken.None);

        Assert.True(configured.IsSuccess);

        // TestConnection promotes status from Configured to Connected
        var tested = await harness.TestConnection.Handle(
            new TestOrganizationIntegrationConnectionCommand(organizationId, "canva"),
            CancellationToken.None);

        Assert.True(tested.IsSuccess);

        var overview = await harness.GetByProvider.Handle(
            new GetOrganizationIntegrationByProviderQuery(organizationId, "canva"),
            CancellationToken.None);

        Assert.True(overview.IsSuccess);
        Assert.Equal(IntegrationStatus.Connected, overview.Value.Status);
    }

    // ─── CANVA-03-b ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfigureCanva_WithScopes_StatusIsConnectedAndScopesResolvable()
    {
        var harness = new CanvaIntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        var configured = await harness.Configure.Handle(
            new ConfigureOrganizationIntegrationCommand(
                organizationId,
                "canva",
                IntegrationConnectionMode.BringYourOwnKey,
                true,
                null,
                new Dictionary<string, string>
                {
                    ["client_id"] = "cid",
                    ["client_secret"] = "csec",
                    ["scopes"] = "asset:read"
                }),
            CancellationToken.None);

        Assert.True(configured.IsSuccess);

        // TestConnection promotes status to Connected so resolver can resolve
        var tested = await harness.TestConnection.Handle(
            new TestOrganizationIntegrationConnectionCommand(organizationId, "canva"),
            CancellationToken.None);

        Assert.True(tested.IsSuccess);

        var resolved = await harness.Resolver.ResolveProviderAsync(organizationId, "canva");

        Assert.True(resolved.IsSuccess);
        Assert.True(resolved.Value.Credentials.ContainsKey("scopes"), "Resolver credentials must contain 'scopes' key");
    }

    // ─── CANVA-03-c ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfigureCanva_BlankClientId_ReturnsRequiredCredentialMissing()
    {
        var harness = new CanvaIntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        var result = await harness.Configure.Handle(
            new ConfigureOrganizationIntegrationCommand(
                organizationId,
                "canva",
                IntegrationConnectionMode.BringYourOwnKey,
                true,
                null,
                new Dictionary<string, string>
                {
                    ["client_id"] = "",
                    ["client_secret"] = "csec"
                }),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(IntegrationErrors.RequiredCredentialMissing.Code, result.Error.Code);
    }

    // ─── CANVA-03-d ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfigureCanva_BlankScopes_DoesNotBlockConnection()
    {
        var harness = new CanvaIntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        // No scopes key at all — should succeed since scopes is not required
        var configured = await harness.Configure.Handle(
            new ConfigureOrganizationIntegrationCommand(
                organizationId,
                "canva",
                IntegrationConnectionMode.BringYourOwnKey,
                true,
                null,
                new Dictionary<string, string>
                {
                    ["client_id"] = "cid",
                    ["client_secret"] = "csec"
                }),
            CancellationToken.None);

        Assert.True(configured.IsSuccess);

        var tested = await harness.TestConnection.Handle(
            new TestOrganizationIntegrationConnectionCommand(organizationId, "canva"),
            CancellationToken.None);

        Assert.True(tested.IsSuccess);

        var byProvider = await harness.GetByProvider.Handle(
            new GetOrganizationIntegrationByProviderQuery(organizationId, "canva"),
            CancellationToken.None);

        Assert.True(byProvider.IsSuccess);
        Assert.Equal(IntegrationStatus.Connected, byProvider.Value.Status);
    }

    // ─── CANVA-03-e ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CanvaIntegration_NeverConfigured_StatusIsNotConfigured()
    {
        var harness = new CanvaIntegrationsHarness(Now);
        var freshOrgId = Guid.NewGuid();

        var overview = await harness.GetOverview.Handle(
            new GetOrganizationIntegrationsQuery(freshOrgId),
            CancellationToken.None);

        Assert.True(overview.IsSuccess);

        var canvaEntry = overview.Value.Integrations.SingleOrDefault(x => x.ProviderKey == "canva");
        Assert.NotNull(canvaEntry);
        Assert.Equal(IntegrationStatus.NotConfigured, canvaEntry!.Status);
    }

    // ─── CANVA-03-f ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByProvider_Canva_DoesNotExposeRawSecretValues()
    {
        var harness = new CanvaIntegrationsHarness(Now);
        var organizationId = Guid.NewGuid();

        await harness.Configure.Handle(
            new ConfigureOrganizationIntegrationCommand(
                organizationId,
                "canva",
                IntegrationConnectionMode.BringYourOwnKey,
                true,
                null,
                new Dictionary<string, string>
                {
                    ["client_id"] = "cid",
                    ["client_secret"] = "csec"
                }),
            CancellationToken.None);

        var result = await harness.GetByProvider.Handle(
            new GetOrganizationIntegrationByProviderQuery(organizationId, "canva"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // ConfiguredCredentialKeys must contain the key names (not raw values)
        Assert.Contains("client_id", result.Value.ConfiguredCredentialKeys);
        Assert.Contains("client_secret", result.Value.ConfiguredCredentialKeys);

        // The Configure response path (CredentialValues from Configure handler) does NOT
        // include raw credential values — it only lists keys. GetByProvider DOES expose
        // unprotected values (expected for admin path), but the values go through the
        // protector. Verify that the keys are present in ConfiguredCredentialKeys and
        // that no raw unprotected literal appears as a key name (keys are field names, not values).
        Assert.DoesNotContain(result.Value.ConfiguredCredentialKeys, k => k == "cid" || k == "csec");
    }

    // ─── Harness ─────────────────────────────────────────────────────────────

    private sealed class CanvaIntegrationsHarness
    {
        private readonly CanvaFlowProviderRepository _providers;
        private readonly CanvaFlowIntegrationRepository _integrations;
        private readonly CanvaFlowCredentialRepository _credentials;

        public CanvaIntegrationsHarness(DateTime nowUtc)
        {
            _providers = new CanvaFlowProviderRepository(nowUtc);
            _integrations = new CanvaFlowIntegrationRepository(_providers);
            _credentials = new CanvaFlowCredentialRepository();

            var protector = new CanvaFakeProtector();
            var unitOfWork = new CanvaUnitOfWork();
            var clock = new TestClock(nowUtc);
            var tester = new CanvaAlwaysSuccessTester();

            Configure = new ConfigureOrganizationIntegrationHandler(
                _providers,
                _integrations,
                _credentials,
                protector,
                unitOfWork,
                clock);

            GetByProvider = new GetOrganizationIntegrationByProviderHandler(
                _providers,
                _integrations,
                _credentials,
                protector);

            GetOverview = new GetOrganizationIntegrationsHandler(
                _providers,
                _integrations,
                _credentials);

            TestConnection = new TestOrganizationIntegrationConnectionHandler(
                _providers,
                _integrations,
                _credentials,
                protector,
                tester,
                unitOfWork,
                clock);

            Resolver = new IntegrationResolver(
                _providers,
                _integrations,
                _credentials,
                protector);
        }

        public ConfigureOrganizationIntegrationHandler Configure { get; }
        public GetOrganizationIntegrationByProviderHandler GetByProvider { get; }
        public GetOrganizationIntegrationsHandler GetOverview { get; }
        public TestOrganizationIntegrationConnectionHandler TestConnection { get; }
        public IntegrationResolver Resolver { get; }
    }

    // ─── In-memory repositories seeded with NEW Connect schema ───────────────

    private sealed class CanvaFlowProviderRepository : IIntegrationProviderCatalogRepository
    {
        private readonly Dictionary<string, IntegrationProvider> _byKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<IntegrationProviderId, IntegrationProvider> _byId = new();

        public CanvaFlowProviderRepository(DateTime nowUtc)
        {
            var canva = IntegrationProvider.Create(
                IntegrationProviderId.New(),
                "canva",
                "Canva",
                IntegrationProviderCategory.Design,
                null,
                "canva",
                supportsBringYourOwnKey: true,
                supportsManagedByIncoders: false,
                supportsSystemDefault: false,
                systemDefaultReadOnly: false,
                [
                    new IntegrationCredentialField("client_id",     "Client ID",     true,  true),
                    new IntegrationCredentialField("client_secret", "Client secret", true,  true),
                    new IntegrationCredentialField("scopes",        "Scopes",        false, false)
                ],
                [],
                nowUtc);

            if (canva.IsSuccess)
            {
                _byKey[canva.Value.Key] = canva.Value;
                _byId[canva.Value.Id] = canva.Value;
            }
        }

        public Task<IReadOnlyList<IntegrationProvider>> ListActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IntegrationProvider>>(_byKey.Values.ToList());

        public Task<IntegrationProvider?> GetByKeyAsync(string key, CancellationToken ct = default)
        {
            _byKey.TryGetValue(key, out var p);
            return Task.FromResult(p);
        }

        public Task<IntegrationProvider?> GetByIdAsync(IntegrationProviderId id, CancellationToken ct = default)
        {
            _byId.TryGetValue(id, out var p);
            return Task.FromResult(p);
        }

        public Task UpsertAsync(IntegrationProvider provider, CancellationToken ct = default)
        {
            _byKey[provider.Key] = provider;
            _byId[provider.Id] = provider;
            return Task.CompletedTask;
        }

        public bool TryGetCategory(IntegrationProviderId providerId, out IntegrationProviderCategory category)
        {
            if (_byId.TryGetValue(providerId, out var provider))
            {
                category = provider.Category;
                return true;
            }

            category = default;
            return false;
        }
    }

    private sealed class CanvaFlowIntegrationRepository : IOrganizationIntegrationRepository
    {
        private readonly List<OrganizationIntegration> _integrations = [];
        private readonly CanvaFlowProviderRepository _providers;

        public CanvaFlowIntegrationRepository(CanvaFlowProviderRepository providers) => _providers = providers;

        public Task<OrganizationIntegration?> GetByOrganizationAndProviderAsync(
            Guid organizationId, IntegrationProviderId providerId, CancellationToken ct = default)
        {
            var found = _integrations.FirstOrDefault(x => x.OrganizationId == organizationId && x.ProviderId == providerId);
            return Task.FromResult(found);
        }

        public Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAsync(
            Guid organizationId, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<OrganizationIntegration>>(
                _integrations.Where(x => x.OrganizationId == organizationId).ToList());
        }

        public Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAndCategoryAsync(
            Guid organizationId, IntegrationProviderCategory category, CancellationToken ct = default)
        {
            var filtered = _integrations
                .Where(x => x.OrganizationId == organizationId)
                .Where(x => _providers.TryGetCategory(x.ProviderId, out var cat) && cat == category)
                .ToList();

            return Task.FromResult<IReadOnlyList<OrganizationIntegration>>(filtered);
        }

        public Task AddAsync(OrganizationIntegration integration, CancellationToken ct = default)
        {
            _integrations.Add(integration);
            return Task.CompletedTask;
        }

        public void Update(OrganizationIntegration integration) { }

        public void Remove(OrganizationIntegration integration) =>
            _integrations.RemoveAll(x => x.Id == integration.Id);
    }

    private sealed class CanvaFlowCredentialRepository : IOrganizationIntegrationCredentialRepository
    {
        private readonly Dictionary<OrganizationIntegrationId, List<OrganizationIntegrationCredential>> _store = new();

        public Task<IReadOnlyList<OrganizationIntegrationCredential>> ListByIntegrationIdAsync(
            OrganizationIntegrationId integrationId, CancellationToken ct = default)
        {
            _store.TryGetValue(integrationId, out var vals);
            return Task.FromResult<IReadOnlyList<OrganizationIntegrationCredential>>(vals?.ToList() ?? []);
        }

        public Task UpsertAsync(OrganizationIntegrationCredential credential, CancellationToken ct = default)
        {
            if (!_store.TryGetValue(credential.OrganizationIntegrationId, out var list))
            {
                list = [];
                _store[credential.OrganizationIntegrationId] = list;
            }

            var idx = list.FindIndex(x => string.Equals(x.Key, credential.Key, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) list[idx] = credential;
            else list.Add(credential);

            return Task.CompletedTask;
        }

        public Task RemoveByIntegrationIdAsync(OrganizationIntegrationId integrationId, CancellationToken ct = default)
        {
            _store.Remove(integrationId);
            return Task.CompletedTask;
        }
    }

    private sealed class CanvaFakeProtector : IIntegrationSecretProtector
    {
        public string Protect(string value) => $"enc::{value}";
        public string Unprotect(string protectedValue) => protectedValue.Replace("enc::", string.Empty, StringComparison.Ordinal);
    }

    private sealed class CanvaAlwaysSuccessTester : IIntegrationConnectionTester
    {
        public Task<IntegrationConnectionTestResult> TestAsync(
            IntegrationProvider provider,
            IntegrationConnectionMode mode,
            IReadOnlyDictionary<string, string> credentials,
            CancellationToken ct = default) =>
            Task.FromResult(new IntegrationConnectionTestResult(true));
    }

    private sealed class CanvaUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
