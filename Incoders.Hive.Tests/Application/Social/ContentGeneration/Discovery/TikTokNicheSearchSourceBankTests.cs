using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// The bank comes FIRST. Every Discover used to re-scrape from scratch and throw the result away —
/// five paid runs bought data the system already held. A paid actor run may now happen for exactly two
/// reasons: the bank is empty, or the operator explicitly asked to refresh.
/// </summary>
public sealed class TikTokNicheSearchSourceBankTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PopulatedBank_ServesStoredCandidates_AndNeverCallsTheResolver()
    {
        var client = MakeClient("Medical & health");
        var bank = new FakeBank();
        bank.Seed(client.OrganizationId, "tiktok", ["medical"], [Banked("k1")]);
        var resolver = new FakeResolver(SourceOutcome.Ran, []);

        var result = await MakeSource(client, resolver, bank).DiscoverAsync(MakeRequest(client));

        Assert.False(resolver.WasCalled);
        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Single(result.Candidates);
        Assert.Equal(DiscoveryOrigin.Bank, result.Origin);
        Assert.Equal(Now.AddDays(-2), result.ObservedAtUtc);
    }

    [Fact]
    public async Task EmptyBank_RunsTheResolver_AndPersistsWhatComesBack()
    {
        var client = MakeClient("Medical & health");
        var bank = new FakeBank();
        var resolver = new FakeResolver(SourceOutcome.Ran, [Discovered("https://www.tiktok.com/@a/video/1")]);

        var result = await MakeSource(client, resolver, bank).DiscoverAsync(MakeRequest(client));

        Assert.True(resolver.WasCalled);
        Assert.Equal(DiscoveryOrigin.Fresh, result.Origin);
        Assert.Single(result.Candidates);

        var recorded = Assert.Single(bank.Recorded);
        Assert.Equal(client.OrganizationId, recorded.OrganizationId);
        Assert.Equal("tiktok", recorded.NetworkKey);
        // Banked under the SAME derived tokens the search used, so an overlapping niche converges.
        Assert.Equal(NicheHashtagDerivation.Derive("Medical & health"), recorded.Hashtags);
        Assert.Equal(
            CandidateIdentity.From("tiktok", "https://www.tiktok.com/@a/video/1"),
            recorded.Observations[0].CandidateKey);
        Assert.Equal(Now, recorded.Observations[0].ObservedAtUtc);
    }

    [Fact]
    public async Task ExplicitRefresh_RunsTheResolver_EvenWithAPopulatedBank()
    {
        var client = MakeClient("Medical & health");
        var bank = new FakeBank();
        bank.Seed(client.OrganizationId, "tiktok", ["medical"], [Banked("k1")]);
        var resolver = new FakeResolver(SourceOutcome.Ran, [Discovered("https://www.tiktok.com/@a/video/9")]);

        var result = await MakeSource(client, resolver, bank)
            .DiscoverAsync(MakeRequest(client) with { ForceRefresh = true });

        Assert.True(resolver.WasCalled);
        Assert.Equal(DiscoveryOrigin.Fresh, result.Origin);
        Assert.Single(bank.Recorded);
    }

    [Fact]
    public async Task TwoClientsOfTheSameOrganization_ShareTheBank()
    {
        var organizationId = Guid.NewGuid();
        var first = MakeClient("Medical & health", organizationId);
        var second = MakeClient("health", organizationId);
        var bank = new FakeBank();

        var firstResolver = new FakeResolver(SourceOutcome.Ran, [Discovered("https://www.tiktok.com/@a/video/1")]);
        var firstResult = await MakeSource(first, firstResolver, bank).DiscoverAsync(MakeRequest(first));

        var secondResolver = new FakeResolver(SourceOutcome.Ran, []);
        var secondResult = await MakeSource(second, secondResolver, bank).DiscoverAsync(MakeRequest(second));

        Assert.True(firstResolver.WasCalled);
        Assert.Equal(DiscoveryOrigin.Fresh, firstResult.Origin);

        // The second client's niche overlaps on "health" — nothing is scraped a second time.
        Assert.False(secondResolver.WasCalled);
        Assert.Equal(DiscoveryOrigin.Bank, secondResult.Origin);
        Assert.Single(secondResult.Candidates);
    }

    [Fact]
    public async Task AFailedRun_IsNeverBanked()
    {
        var client = MakeClient("Medical & health");
        var bank = new FakeBank();
        var resolver = new FakeResolver(SourceOutcome.Failed, []);

        var result = await MakeSource(client, resolver, bank).DiscoverAsync(MakeRequest(client));

        Assert.Equal(SourceOutcome.Failed, result.Outcome);
        Assert.Empty(bank.Recorded);
    }

    [Fact]
    public async Task ABankWriteFailure_NeverLosesTheCandidatesTheRunAlreadyPaidFor()
    {
        var client = MakeClient("Medical & health");
        var bank = new FakeBank { ThrowOnRecord = true };
        var resolver = new FakeResolver(SourceOutcome.Ran, [Discovered("https://www.tiktok.com/@a/video/1")]);

        var result = await MakeSource(client, resolver, bank).DiscoverAsync(MakeRequest(client));

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public async Task NetworksExcludingTikTok_NeverTouchTheBank()
    {
        var client = MakeClient("Medical & health");
        var bank = new FakeBank();
        bank.Seed(client.OrganizationId, "tiktok", ["medical"], [Banked("k1")]);
        var resolver = new FakeResolver(SourceOutcome.Ran, []);

        var result = await MakeSource(client, resolver, bank)
            .DiscoverAsync(MakeRequest(client) with { NetworkSources = ["instagram"] });

        Assert.Empty(result.Candidates);
        Assert.False(bank.WasRead);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static TikTokNicheSearchSource MakeSource(Client client, INicheSearchResolver resolver, IContentCandidateRepository bank) =>
        new(
            new FakeClients(client),
            resolver,
            bank,
            new FixedClock(Now.UtcDateTime),
            // No catalog niche assigned: these cases exercise the legacy free-text path, unchanged.
            StubClientNicheHashtagProvider.NoNiches());

    private static Client MakeClient(string? sectorOrNiche, Guid? organizationId = null) =>
        Client.Create(ClientId.New(), organizationId ?? Guid.NewGuid(), "Acme", null, DateTime.UtcNow, sectorOrNiche).Value;

    private static ContentDiscoveryRequest MakeRequest(Client client) =>
        new(client.OrganizationId, client.Id.Value, ["tiktok"], MaxResults: 20, SearchCriterion: "niche");

    private static DiscoveredContent Discovered(string url) =>
        new(
            "Título", "resumen", "tiktok", url, null,
            Views: 1000, Likes: 10, Comments: 1, Shares: null, Saves: null,
            PublishedAtUtc: Now.AddDays(-3), SourceHandle: "@a", SourceKey: "niche");

    private static CandidateObservation Banked(string key) =>
        new(
            CandidateKey: key,
            NetworkKey: "tiktok",
            SourceKey: "niche",
            SearchCriterion: "niche",
            ContentUrl: $"https://tiktok.com/@a/video/{key}",
            AuthorHandle: "@a",
            Title: "banked",
            Summary: "banked summary",
            ThumbnailUrl: null,
            PublishedAtUtc: Now.AddDays(-9),
            Views: 4_000,
            Likes: 40,
            Comments: 4,
            Shares: null,
            Saves: null,
            OpportunityScore: 0.5,
            Classification: 1,
            ObservedAtUtc: Now.AddDays(-2));

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }

    internal sealed record BankWrite(
        Guid OrganizationId,
        string NetworkKey,
        IReadOnlyList<string> Hashtags,
        IReadOnlyList<CandidateObservation> Observations);

    private sealed class FakeBank : IContentCandidateRepository
    {
        private readonly List<(Guid OrganizationId, string NetworkKey, string Hashtag, CandidateObservation Observation)> _rows = [];

        public List<BankWrite> Recorded { get; } = [];

        public bool WasRead { get; private set; }

        public bool ThrowOnRecord { get; init; }

        public void Seed(Guid organizationId, string networkKey, IReadOnlyList<string> hashtags, IReadOnlyList<CandidateObservation> observations)
        {
            foreach (var hashtag in hashtags)
            {
                foreach (var observation in observations)
                {
                    _rows.Add((organizationId, networkKey, hashtag, observation));
                }
            }
        }

        public Task<IReadOnlyList<CandidateObservation>> GetNicheBankAsync(
            Guid organizationId,
            string networkKey,
            IReadOnlyList<string> hashtags,
            int maxResults,
            CancellationToken ct = default)
        {
            WasRead = true;

            var matches = _rows
                .Where(row => row.OrganizationId == organizationId
                              && string.Equals(row.NetworkKey, networkKey, StringComparison.OrdinalIgnoreCase)
                              && hashtags.Contains(row.Hashtag, StringComparer.Ordinal))
                .Select(row => row.Observation)
                .DistinctBy(observation => observation.CandidateKey)
                .OrderByDescending(observation => observation.Views ?? -1)
                .Take(maxResults)
                .ToList();

            return Task.FromResult<IReadOnlyList<CandidateObservation>>(matches);
        }

        public Task<int> RecordNicheBankAsync(
            Guid organizationId,
            string networkKey,
            IReadOnlyList<string> hashtags,
            IReadOnlyList<CandidateObservation> observations,
            CancellationToken ct = default)
        {
            if (ThrowOnRecord)
            {
                throw new InvalidOperationException("bank unavailable");
            }

            Recorded.Add(new BankWrite(organizationId, networkKey, hashtags, observations));
            Seed(organizationId, networkKey, hashtags, observations);

            return Task.FromResult(observations.Count);
        }

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
    }

    private sealed class FakeClients : ISocialClientRepository
    {
        private readonly Client? _client;

        public FakeClients(Client? client) => _client = client;

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
