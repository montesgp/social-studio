using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Features.Integrations.Actors;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Tests.Common;

namespace SocialStudio.Tests.Application.Integrations;

/// <summary>
/// Actor catalog sync — mirrors the AI-model sync so an operator can press "Sync" on the Apify
/// connector and get the org's available actors instead of pasting ids by hand.
///
/// The catalog is a PICKER SOURCE only: routing still resolves from the `actor_mappings_json`
/// credential, which this feature never reads nor writes.
/// </summary>
public sealed class OrganizationActorSyncTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Sync_PersistsProviderActors_AndListQueryReturnsThem()
    {
        var organizationId = Guid.NewGuid();
        var provider = Provider("apify", IntegrationProviderCategory.DataExtraction);
        var integration = ConnectedIntegration(organizationId, provider.Id);
        var providers = new ProviderCatalog(provider);
        var store = new ActorStore();

        var handler = new SyncOrganizationActorsHandler(
            providers,
            new IntegrationStore(integration),
            new StaticIntegrationResolver(provider.Key),
            new StaticExternalClientFactory(new StaticActorCatalogClient(
            [
                new ProviderActor("aBcDeFgHiJkLmNoP1", "own-scraper", "acme", "Own Scraper", IsOwned: true, new Dictionary<string, string>()),
                new ProviderActor("zZ9YyXxWwVvUuTtS2", "store-scraper", "vendor", "Store Scraper", IsOwned: false, new Dictionary<string, string>())
            ])),
            store,
            new UnitOfWorkSpy(),
            new TestClock(Now));

        var sync = await handler.Handle(
            new SyncOrganizationActorsCommand(organizationId, provider.Key),
            CancellationToken.None);

        Assert.True(sync.IsSuccess);
        Assert.Equal(["Own Scraper", "Store Scraper"], sync.Value.Actors.Select(x => x.DisplayName));
        Assert.All(sync.Value.Actors, actor => Assert.Equal(organizationId, actor.OrganizationId));

        // Store actors the org merely USES must survive the sync: the current TikTok routing depends
        // on a public-store actor the org does not own.
        var storeActor = sync.Value.Actors.Single(x => x.ActorId == "zZ9YyXxWwVvUuTtS2");
        Assert.False(storeActor.IsOwned);
        Assert.Equal("vendor", storeActor.Username);
        Assert.Equal("vendor/store-scraper", storeActor.Slug);

        var ownActor = sync.Value.Actors.Single(x => x.ActorId == "aBcDeFgHiJkLmNoP1");
        Assert.True(ownActor.IsOwned);

        var list = new ListOrganizationActorsHandler(providers, store);
        var listed = await list.Handle(
            new ListOrganizationActorsQuery(organizationId, provider.Key),
            CancellationToken.None);

        Assert.True(listed.IsSuccess);
        Assert.Equal(["Own Scraper", "Store Scraper"], listed.Value.Actors.Select(x => x.DisplayName));
        Assert.Equal(Now, listed.Value.LastSyncedAtUtc);
    }

    [Fact]
    public async Task Sync_MarksActorsMissingFromTheProviderAsUnavailable()
    {
        var organizationId = Guid.NewGuid();
        var provider = Provider("apify", IntegrationProviderCategory.DataExtraction);
        var integration = ConnectedIntegration(organizationId, provider.Id);
        var providers = new ProviderCatalog(provider);
        var store = new ActorStore();

        async Task SyncWithAsync(params ProviderActor[] actors)
        {
            var handler = new SyncOrganizationActorsHandler(
                providers,
                new IntegrationStore(integration),
                new StaticIntegrationResolver(provider.Key),
                new StaticExternalClientFactory(new StaticActorCatalogClient(actors)),
                store,
                new UnitOfWorkSpy(),
                new TestClock(Now));

            var result = await handler.Handle(
                new SyncOrganizationActorsCommand(organizationId, provider.Key),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        await SyncWithAsync(
            new ProviderActor("aBcDeFgHiJkLmNoP1", "kept", "acme", "Kept", IsOwned: true, new Dictionary<string, string>()),
            new ProviderActor("zZ9YyXxWwVvUuTtS2", "dropped", "acme", "Dropped", IsOwned: true, new Dictionary<string, string>()));

        await SyncWithAsync(
            new ProviderActor("aBcDeFgHiJkLmNoP1", "kept", "acme", "Kept", IsOwned: true, new Dictionary<string, string>()));

        var list = new ListOrganizationActorsHandler(providers, store);
        var listed = await list.Handle(
            new ListOrganizationActorsQuery(organizationId, provider.Key),
            CancellationToken.None);

        Assert.True(listed.IsSuccess);
        Assert.Equal(["Kept"], listed.Value.Actors.Select(x => x.DisplayName));
    }

    [Fact]
    public async Task Sync_RejectsProviderWithoutActorCatalogClient()
    {
        var organizationId = Guid.NewGuid();
        var provider = Provider("claude", IntegrationProviderCategory.AiModel);

        var handler = new SyncOrganizationActorsHandler(
            new ProviderCatalog(provider),
            new IntegrationStore(ConnectedIntegration(organizationId, provider.Id)),
            new StaticIntegrationResolver(provider.Key),
            new StaticExternalClientFactory(client: null, supports: false),
            new ActorStore(),
            new UnitOfWorkSpy(),
            new TestClock(Now));

        var result = await handler.Handle(
            new SyncOrganizationActorsCommand(organizationId, provider.Key),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ExternalProviderUnsupported.Code, result.Error.Code);
    }

    [Fact]
    public async Task Sync_RejectsIntegrationThatIsNotConnected()
    {
        var organizationId = Guid.NewGuid();
        var provider = Provider("apify", IntegrationProviderCategory.DataExtraction);

        var handler = new SyncOrganizationActorsHandler(
            new ProviderCatalog(provider),
            new IntegrationStore(),
            new StaticIntegrationResolver(provider.Key),
            new StaticExternalClientFactory(new StaticActorCatalogClient([])),
            new ActorStore(),
            new UnitOfWorkSpy(),
            new TestClock(Now));

        var result = await handler.Handle(
            new SyncOrganizationActorsCommand(organizationId, provider.Key),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.IntegrationNotConnected.Code, result.Error.Code);
    }

    [Fact]
    public async Task Sync_RejectsUnknownProviderKey()
    {
        var handler = new SyncOrganizationActorsHandler(
            new ProviderCatalog(),
            new IntegrationStore(),
            new StaticIntegrationResolver("apify"),
            new StaticExternalClientFactory(new StaticActorCatalogClient([])),
            new ActorStore(),
            new UnitOfWorkSpy(),
            new TestClock(Now));

        var result = await handler.Handle(
            new SyncOrganizationActorsCommand(Guid.NewGuid(), "nope"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderNotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task List_ReturnsEmptyCatalogBeforeAnySync()
    {
        var organizationId = Guid.NewGuid();
        var provider = Provider("apify", IntegrationProviderCategory.DataExtraction);

        var listed = await new ListOrganizationActorsHandler(new ProviderCatalog(provider), new ActorStore())
            .Handle(new ListOrganizationActorsQuery(organizationId, provider.Key), CancellationToken.None);

        Assert.True(listed.IsSuccess);
        Assert.Empty(listed.Value.Actors);
        Assert.Null(listed.Value.LastSyncedAtUtc);
    }

    private static IntegrationProvider Provider(string key, IntegrationProviderCategory category)
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
            [],
            Now);

        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private static OrganizationIntegration ConnectedIntegration(Guid organizationId, IntegrationProviderId providerId)
    {
        var created = OrganizationIntegration.Create(
            OrganizationIntegrationId.New(),
            organizationId,
            providerId,
            IntegrationConnectionMode.BringYourOwnKey,
            IntegrationStatus.Connected,
            isReadOnly: false,
            selectedAiModel: null,
            lastTestedAtUtc: Now,
            lastError: null,
            Now);

        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private sealed class ProviderCatalog : IIntegrationProviderCatalogRepository
    {
        private readonly Dictionary<string, IntegrationProvider> _byKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<IntegrationProviderId, IntegrationProvider> _byId = [];

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

    private sealed class ActorStore : IOrganizationActorRepository
    {
        private readonly List<OrganizationActorRecord> _items = [];

        public Task<IReadOnlyList<OrganizationActorRecord>> ListByProviderAsync(
            Guid organizationId,
            Guid providerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationActorRecord>>(
                _items
                    .Where(x => x.OrganizationId == organizationId && x.ProviderId == providerId && x.IsAvailable)
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        public Task UpsertManyAsync(
            IReadOnlyList<OrganizationActorRecord> actors,
            CancellationToken cancellationToken = default)
        {
            if (actors.Count == 0)
            {
                return Task.CompletedTask;
            }

            var organizationId = actors[0].OrganizationId;
            var providerId = actors[0].ProviderId;
            var incoming = actors.Select(x => x.ActorId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < _items.Count; i++)
            {
                var existing = _items[i];
                if (existing.OrganizationId == organizationId
                    && existing.ProviderId == providerId
                    && !incoming.Contains(existing.ActorId))
                {
                    _items[i] = existing with { IsAvailable = false };
                }
            }

            foreach (var actor in actors)
            {
                _items.RemoveAll(x =>
                    x.OrganizationId == actor.OrganizationId
                    && x.ProviderId == actor.ProviderId
                    && string.Equals(x.ActorId, actor.ActorId, StringComparison.OrdinalIgnoreCase));
                _items.Add(actor);
            }

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
                IntegrationProviderCategory.DataExtraction,
                IntegrationConnectionMode.BringYourOwnKey,
                IntegrationStatus.Connected,
                IsReadOnly: false,
                SelectedAiModel: null,
                new Dictionary<string, string> { ["api_key"] = "test-key" })));
    }

    private sealed class StaticExternalClientFactory : IExternalProviderClientFactory
    {
        private readonly IActorCatalogClient? _client;
        private readonly bool _supports;

        public StaticExternalClientFactory(IActorCatalogClient? client, bool supports = true)
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

    private sealed class StaticActorCatalogClient : IActorCatalogClient
    {
        private readonly IReadOnlyList<ProviderActor> _actors;

        public StaticActorCatalogClient(IReadOnlyList<ProviderActor> actors) => _actors = actors;

        public Task<Result<IReadOnlyList<ProviderActor>>> ListActorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(_actors));
    }

    private sealed class UnitOfWorkSpy : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
