using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// Curated niche hashtags — not the free-text sector — are what a niche search asks the network for,
/// and therefore what the candidate bank is keyed by. The free-text derivation survives only as the
/// fallback for a client with no niche assigned.
/// </summary>
public sealed class TikTokNicheSearchSourceCuratedHashtagTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClientWithNiches_SearchesTheCuratedHashtags_NotTheDerivedFreeText()
    {
        var client = MakeClient("Medical & health");
        var resolver = new CapturingResolver(SourceOutcome.Ran, [Discovered("https://www.tiktok.com/@a/video/1")]);
        var bank = new FakeBank();

        var result = await MakeSource(client, resolver, bank, StubClientNicheHashtagProvider.Curated("medical", "health"))
            .DiscoverAsync(MakeRequest(client));

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Equal(["medical", "health"], resolver.LastRequest!.Hashtags);
        // The legacy derivation would also have offered "medicalhealth", the tag that does not exist.
        Assert.DoesNotContain("medicalhealth", resolver.LastRequest!.Hashtags!);

        var recorded = Assert.Single(bank.Recorded);
        Assert.Equal(["medical", "health"], recorded.Hashtags);
    }

    [Fact]
    public async Task ClientWithoutNiches_StillFallsBackToTheFreeTextDerivation()
    {
        var client = MakeClient("Medical & health");
        var resolver = new CapturingResolver(SourceOutcome.Ran, []);

        var result = await MakeSource(client, resolver, new FakeBank(), StubClientNicheHashtagProvider.NoNiches())
            .DiscoverAsync(MakeRequest(client));

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Equal("Medical & health", resolver.LastRequest!.SearchTerm);
        Assert.Equal(NicheHashtagDerivation.Derive("Medical & health"), resolver.LastRequest!.Hashtags);
    }

    [Fact]
    public async Task ClientWhoseNichesHaveNoCuratedHashtags_ReportsUnconfigured_AndNeverRunsTheProvider()
    {
        var client = MakeClient("Medical & health");
        var resolver = new CapturingResolver(SourceOutcome.Ran, []);
        var bank = new FakeBank();

        var result = await MakeSource(client, resolver, bank, StubClientNicheHashtagProvider.MissingCuratedHashtags())
            .DiscoverAsync(MakeRequest(client));

        // A niche with no curated hashtag is an unfinished catalog entry, so the operator is sent to
        // the catalog — it must NOT silently degrade into the free-text search it was meant to replace.
        Assert.Equal(SourceOutcome.Unconfigured, result.Outcome);
        Assert.Empty(result.Candidates);
        Assert.False(resolver.WasCalled);
        Assert.False(bank.WasRead);
    }

    [Fact]
    public async Task ClientWithNichesButNoFreeTextSector_StillSearches()
    {
        var client = MakeClient(sectorOrNiche: null);
        var resolver = new CapturingResolver(SourceOutcome.Ran, []);

        var result = await MakeSource(client, resolver, new FakeBank(), StubClientNicheHashtagProvider.Curated("gastronomy", "food"))
            .DiscoverAsync(MakeRequest(client));

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.True(resolver.WasCalled);
        Assert.Equal(["gastronomy", "food"], resolver.LastRequest!.Hashtags);
    }

    [Fact]
    public async Task TwoClientsSharingANiche_ShareTheBank_SoTheSecondRunsNoProviderCall()
    {
        var organizationId = Guid.NewGuid();
        var first = MakeClient("Clínica Norte", organizationId);
        // Deliberately unrelated free text: under the legacy derivation these two would never converge.
        var second = MakeClient("Centro médico Sur", organizationId);
        var bank = new FakeBank();

        var firstResolver = new CapturingResolver(SourceOutcome.Ran, [Discovered("https://www.tiktok.com/@a/video/1")]);
        var firstResult = await MakeSource(first, firstResolver, bank, StubClientNicheHashtagProvider.Curated("medical", "health"))
            .DiscoverAsync(MakeRequest(first));

        var secondResolver = new CapturingResolver(SourceOutcome.Ran, []);
        var secondResult = await MakeSource(second, secondResolver, bank, StubClientNicheHashtagProvider.Curated("medical", "health"))
            .DiscoverAsync(MakeRequest(second));

        Assert.True(firstResolver.WasCalled);
        Assert.Equal(DiscoveryOrigin.Fresh, firstResult.Origin);

        Assert.False(secondResolver.WasCalled);
        Assert.Equal(DiscoveryOrigin.Bank, secondResult.Origin);
        Assert.Single(secondResult.Candidates);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static TikTokNicheSearchSource MakeSource(
        Client client,
        INicheSearchResolver resolver,
        IContentCandidateRepository bank,
        IClientNicheHashtagProvider nicheHashtags) =>
        new(new FakeClients(client), resolver, bank, new FixedClock(Now.UtcDateTime), nicheHashtags);

    private static Client MakeClient(string? sectorOrNiche, Guid? organizationId = null) =>
        Client.Create(ClientId.New(), organizationId ?? Guid.NewGuid(), "Acme", null, DateTime.UtcNow, sectorOrNiche).Value;

    private static ContentDiscoveryRequest MakeRequest(Client client) =>
        new(client.OrganizationId, client.Id.Value, ["tiktok"], MaxResults: 20, SearchCriterion: "niche");

    private static DiscoveredContent Discovered(string url) =>
        new(
            "Título", "resumen", "tiktok", url, null,
            Views: 1000, Likes: 10, Comments: 1, Shares: null, Saves: null,
            PublishedAtUtc: Now.AddDays(-3), SourceHandle: "@a", SourceKey: "niche");

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
            Recorded.Add(new BankWrite(organizationId, networkKey, hashtags, observations));

            foreach (var hashtag in hashtags)
            {
                foreach (var observation in observations)
                {
                    _rows.Add((organizationId, networkKey, hashtag, observation));
                }
            }

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

    private sealed class CapturingResolver : INicheSearchResolver
    {
        private readonly SourceOutcome _outcome;
        private readonly IReadOnlyList<DiscoveredContent> _candidates;

        public CapturingResolver(SourceOutcome outcome, IReadOnlyList<DiscoveredContent> candidates)
        {
            _outcome = outcome;
            _candidates = candidates;
        }

        public bool WasCalled { get; private set; }

        public NicheSearchRequest? LastRequest { get; private set; }

        public Task<NicheSearchResolution> SearchAsync(NicheSearchRequest request, CancellationToken ct = default)
        {
            WasCalled = true;
            LastRequest = request;
            return Task.FromResult(new NicheSearchResolution(_outcome, _candidates));
        }
    }
}
