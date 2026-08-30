using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientOwnAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Discovery;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// WU-3: <see cref="OwnAccountDiscoverySource"/> is a near-clone of
/// <see cref="ReferenceAccountDiscoverySource"/>, feeding the same <c>IProfileScrapeResolver</c> loop
/// from <see cref="IClientOwnAccountRepository"/> instead of the referent list. <c>Order: 2</c>,
/// <c>Supports("own")</c> only (CR-DISC-02).
/// </summary>
public sealed class OwnAccountDiscoverySourceTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly SocialNetworkId InstagramNetworkId = SocialNetworkId.New();
    private static readonly SocialNetworkId TikTokNetworkId = SocialNetworkId.New();

    [Fact]
    public void SourceKey_And_Order_And_Supports()
    {
        var source = Build([], [], new Dictionary<string, Result<ProfileScrapeResult>>());

        Assert.Equal("own", source.SourceKey);
        Assert.Equal(2, source.Order);
        Assert.True(source.Supports("own"));
        Assert.False(source.Supports("references"));
        Assert.False(source.Supports("niche"));
    }

    [Fact]
    public async Task Discover_ReturnsRealMetrics_FromTheClientsOwnInstagramAccount()
    {
        var account = OwnAccount("@mine", InstagramNetworkId);
        var source = Build(
            [account],
            [Network(InstagramNetworkId, "instagram")],
            Scrape("@mine", Post("Un reel propio", views: 50_000, likes: 1_200, comments: 90)));

        var result = await source.DiscoverAsync(Request(10), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        var item = Assert.Single(result.Candidates);
        Assert.Equal(50_000, item.Views);
        Assert.Equal("@mine", item.SourceHandle);
        Assert.Equal("own", item.SourceKey);
    }

    [Fact]
    public async Task Discover_ReturnsEmptyNonThrowingResult_ForATikTokOnlyClient()
    {
        var account = OwnAccount("@tiktokonly", TikTokNetworkId);
        var source = Build(
            [account],
            [Network(TikTokNetworkId, "tiktok")],
            new Dictionary<string, Result<ProfileScrapeResult>>());

        var result = await source.DiscoverAsync(Request(10), CancellationToken.None);

        // CR-DISC-02 scenario 2: instagram-only scraping today; a TikTok-only own client is an
        // honest, non-throwing empty run, never an exception.
        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Discover_ReturnsEmptyRan_WhenTheClientHasNoOwnAccounts()
    {
        var source = Build([], [], new Dictionary<string, Result<ProfileScrapeResult>>());

        var result = await source.DiscoverAsync(Request(10), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static ContentDiscoveryRequest Request(int max) => new(Org, Client, [], max, "own");

    private static ClientOwnAccount OwnAccount(string handle, SocialNetworkId networkId) =>
        ClientOwnAccount.Create(
            ClientOwnAccountId.New(),
            new ClientId(Client),
            Org,
            networkId,
            profileUrl: $"https://social.com/{handle}",
            handle: handle,
            label: null,
            notes: null,
            nowUtc: DateTime.UtcNow).Value;

    private static SocialNetwork Network(SocialNetworkId id, string code) =>
        SocialNetworkRestore(id, code);

    private static SocialNetwork SocialNetworkRestore(SocialNetworkId id, string code) =>
        SocialNetwork.Restore(id, code, code, code.Length > 32 ? code[..32] : code, true, DateTime.UtcNow, DateTime.UtcNow);

    private static OwnAccountDiscoverySource Build(
        IReadOnlyList<ClientOwnAccount> accounts,
        IReadOnlyList<SocialNetwork> networks,
        IReadOnlyDictionary<string, Result<ProfileScrapeResult>> scrapes) =>
        new(new StubOwnAccountRepository(accounts), new StubNetworkRepository(networks), new StubScraper(scrapes));

    private static ProfilePost Post(string caption, long? views, long likes = 10, long comments = 1) =>
        new(
            PostId: Guid.NewGuid().ToString(),
            MediaType: "Video",
            LikesCount: likes,
            CommentsCount: comments,
            VideoViewCount: views,
            Timestamp: DateTimeOffset.UtcNow,
            OwnerId: "owner",
            OwnerUsername: "owner",
            IsPaidPartnership: false,
            IsDegraded: false,
            Hashtags: [],
            Caption: caption);

    private static Dictionary<string, Result<ProfileScrapeResult>> Scrape(string handle, params ProfilePost[] posts) =>
        new()
        {
            [handle] = Result.Success(new ProfileScrapeResult(
                Profile: new ProfileSnapshot("id", handle, 0, 0, null, 0, null, null, null, false, false, null),
                OwnerPosts: posts,
                Reels: [],
                ProviderAggregates: new ProfileAggregates(0, 0, 0, posts.Length, posts.Length, 0),
                IsPartial: false,
                Diagnostics: []))
        };

    private sealed class StubOwnAccountRepository(IReadOnlyList<ClientOwnAccount> accounts) : IClientOwnAccountRepository
    {
        public Task<ClientOwnAccount?> GetByIdAsync(ClientOwnAccountId id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<ClientOwnAccount?>(accounts.FirstOrDefault(a => a.Id == id));

        public Task<(IReadOnlyList<ClientOwnAccount> Items, int TotalCount)> ListByClientAsync(
            ClientId clientId, Guid organizationId, bool includeInactive, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<ClientOwnAccount>, int)>((accounts, accounts.Count));

        public Task<IReadOnlyList<ClientOwnAccount>> GetAllByClientAsync(ClientId clientId, Guid organizationId, CancellationToken ct) =>
            Task.FromResult(accounts);

        public Task<bool> ExistsByProfileUrlAsync(ClientId clientId, SocialNetworkId socialNetworkId, string profileUrl, ClientOwnAccountId? excludeId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task AddAsync(ClientOwnAccount ownAccount, CancellationToken ct) => Task.CompletedTask;

        public void Update(ClientOwnAccount account) { }

        public Task ReorderAsync(ClientId clientId, Guid organizationId, IReadOnlyList<Guid> orderedIds, CancellationToken ct) =>
            Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubNetworkRepository(IReadOnlyList<SocialNetwork> networks) : ISocialNetworkRepository
    {
        public Task<SocialNetwork?> GetByIdAsync(SocialNetworkId id, CancellationToken cancellationToken) =>
            Task.FromResult(networks.FirstOrDefault(n => n.Id == id));

        public Task<SocialNetwork?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(networks.FirstOrDefault(n => string.Equals(n.Code, code, StringComparison.OrdinalIgnoreCase)));

        public Task<Incoders.Hive.Application.Common.Pagination.PageResult<SocialNetwork>> ListAsync(
            SocialNetworkListQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new Incoders.Hive.Application.Common.Pagination.PageResult<SocialNetwork>(networks, 1, networks.Count, networks.Count));

        public Task<IReadOnlyList<SocialNetwork>> GetByIdsAsync(IReadOnlyCollection<SocialNetworkId> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SocialNetwork>>(networks.Where(n => ids.Contains(n.Id)).ToList());

        public Task AddAsync(SocialNetwork network, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Update(SocialNetwork network) { }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubScraper(IReadOnlyDictionary<string, Result<ProfileScrapeResult>> scrapes) : IProfileScrapeResolver
    {
        public Task<Result<ProfileScrapeResult>> ResolveAsync(ProfileScrapeRequest request, CancellationToken ct = default) =>
            Task.FromResult(
                scrapes.TryGetValue(request.ProfileUrlOrHandle, out var result)
                    ? result
                    : Result.Failure<ProfileScrapeResult>(Error.NotFound("scrape.unknown", "no stub")));
    }
}
