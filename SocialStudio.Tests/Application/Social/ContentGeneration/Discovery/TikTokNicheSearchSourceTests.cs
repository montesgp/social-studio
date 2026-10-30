using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Features.Social.ContentGeneration.Discovery;
using SocialStudio.Domain.Social.Clients;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Discovery;

public sealed class TikTokNicheSearchSourceTests
{
    [Fact]
    public void SourceKeyOrderAndSupports_MatchDesign()
    {
        var source = MakeSource(new FakeClients(null), new FakeResolver(SourceOutcome.Ran, []));

        Assert.Equal("niche", source.SourceKey);
        Assert.Equal(3, source.Order);
        Assert.True(source.Supports("niche"));
        Assert.False(source.Supports("references"));
        Assert.False(source.Supports("own"));
    }

    [Fact]
    public async Task DiscoverAsync_NoEnabledRoute_ReportsUnconfigured_NotFailed()
    {
        var client = MakeClient("skincare brands");
        var source = MakeSource(new FakeClients(client),new FakeResolver(SourceOutcome.Unconfigured, []));

        var result = await source.DiscoverAsync(MakeRequest(client));

        // CD-13/D12: Unconfigured lands as its own outcome, distinct from Failed.
        Assert.Equal(SourceOutcome.Unconfigured, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task DiscoverAsync_ConfiguredAndFound_ReturnsRanWithCandidates()
    {
        var client = MakeClient("skincare brands");
        var discovered = new DiscoveredContent(
            "Título", "resumen", "tiktok", "https://www.tiktok.com/@a/video/1", null,
            Views: 1000, Likes: 10, Comments: 1, Shares: null, Saves: null,
            PublishedAtUtc: DateTimeOffset.UtcNow, SourceHandle: "@a", SourceKey: "niche");

        var source = MakeSource(new FakeClients(client),new FakeResolver(SourceOutcome.Ran, [discovered]));

        var result = await source.DiscoverAsync(MakeRequest(client));

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public async Task DiscoverAsync_ClientWithNoSectorOrNiche_ReturnsRanEmpty_NotUnconfigured()
    {
        var client = MakeClient(sectorOrNiche: null);
        var resolver = new FakeResolver(SourceOutcome.Ran, []);
        var source = MakeSource(new FakeClients(client),resolver);

        var result = await source.DiscoverAsync(MakeRequest(client));

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Empty(result.Candidates);
        Assert.False(resolver.WasCalled);
    }

    [Fact]
    public async Task DiscoverAsync_NetworksExcludeTikTok_ReturnsRanEmpty_NeverCallsTheResolver()
    {
        var client = MakeClient("skincare brands");
        var resolver = new FakeResolver(SourceOutcome.Ran, []);
        var source = MakeSource(new FakeClients(client),resolver);

        var result = await source.DiscoverAsync(MakeRequest(client) with { NetworkSources = ["instagram"] });

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Empty(result.Candidates);
        Assert.False(resolver.WasCalled);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the source over an EMPTY candidate bank, which is the only condition (besides an explicit
    /// operator refresh) under which the resolver is reached at all. The bank's own behaviour is
    /// covered by <see cref="TikTokNicheSearchSourceBankTests"/>.
    /// </summary>
    private static TikTokNicheSearchSource MakeSource(ISocialClientRepository clients, INicheSearchResolver resolver) =>
        new(
            clients,
            resolver,
            new EmptyBank(),
            new FixedClock(new DateTime(2026, 8, 12, 9, 0, 0, DateTimeKind.Utc)),
            // No catalog niche assigned: these cases are the legacy free-text path, unchanged.
            StubClientNicheHashtagProvider.NoNiches());

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }

    private sealed class EmptyBank : IContentCandidateRepository
    {
        public Task RecordObservationsAsync(
            Guid organizationId,
            Guid clientId,
            IReadOnlyList<CandidateObservation> observations,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<CandidateObservation>> GetLatestByKeysAsync(
            Guid organizationId,
            Guid clientId,
            IReadOnlyList<string> candidateKeys,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CandidateObservation>>([]);

        public Task<IReadOnlyList<CandidateObservation>> GetNicheBankAsync(
            Guid organizationId,
            string networkKey,
            IReadOnlyList<string> hashtags,
            int maxResults,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CandidateObservation>>([]);

        public Task<int> RecordNicheBankAsync(
            Guid organizationId,
            string networkKey,
            IReadOnlyList<string> hashtags,
            IReadOnlyList<CandidateObservation> observations,
            CancellationToken ct = default) => Task.FromResult(observations.Count);
    }

    private static Client MakeClient(string? sectorOrNiche) =>
        Client.Create(ClientId.New(), Guid.NewGuid(), "Acme", null, DateTime.UtcNow, sectorOrNiche).Value;

    private static ContentDiscoveryRequest MakeRequest(Client client) =>
        new(client.OrganizationId, client.Id.Value, ["tiktok"], MaxResults: 20, SearchCriterion: "niche");

    private sealed class FakeClients : ISocialClientRepository
    {
        private readonly Client? _client;

        public FakeClients(Client? client)
        {
            _client = client;
        }

        public Task<Client?> GetByIdAsync(ClientId id, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_client);

        public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(SocialClientQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<Client>, int)>(([], 0));

        public Task AddAsync(Client client, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Update(Client client)
        {
        }
    }

    private sealed class FakeResolver : INicheSearchResolver
    {
        private readonly SourceOutcome _outcome;
        private readonly IReadOnlyList<DiscoveredContent> _candidates;

        public FakeResolver(SourceOutcome outcome, IReadOnlyList<DiscoveredContent> candidates)
        {
            _outcome = outcome;
            _candidates = candidates;
        }

        public bool WasCalled { get; private set; }

        public Task<NicheSearchResolution> SearchAsync(NicheSearchRequest request, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(new NicheSearchResolution(_outcome, _candidates));
        }
    }
}
