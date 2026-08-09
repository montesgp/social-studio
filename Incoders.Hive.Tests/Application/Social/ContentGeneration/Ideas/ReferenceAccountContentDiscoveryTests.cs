using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Ideas;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Ideas;

/// <summary>
/// The Content Generator's PROPOSAL phase: bring back real content with real metrics so the operator
/// can pick what to generate from.
///
/// Unlike the idea sources, this keeps the full post — views, likes, comments, thumbnail — because a
/// proposal the operator cannot judge is just a list.
/// </summary>
public sealed class ReferenceAccountContentDiscoveryTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();

    [Fact]
    public async Task Discover_ReturnsRealMetrics()
    {
        var service = Build(["@ref"], Scrape("@ref", Post("Un reel", views: 120_000, likes: 4_000, comments: 210)));

        var found = await service.DiscoverAsync(Request(10), CancellationToken.None);

        var item = Assert.Single(found);
        Assert.Equal(120_000, item.Views);
        Assert.Equal(4_000, item.Likes);
        Assert.Equal(210, item.Comments);
        Assert.Equal("@ref", item.SourceHandle);
    }

    [Fact]
    public async Task Discover_RanksTheStrongestFirst()
    {
        var service = Build(["@ref"], Scrape(
            "@ref",
            Post("flojo", views: 900),
            Post("ganador", views: 500_000),
            Post("medio", views: 30_000)));

        var found = await service.DiscoverAsync(Request(10), CancellationToken.None);

        Assert.Equal(["ganador", "medio", "flojo"], found.Select(f => f.Title).ToArray());
    }

    [Fact]
    public async Task Discover_RespectsTheResultCap()
    {
        var service = Build(["@ref"], Scrape(
            "@ref",
            Post("a", views: 10),
            Post("b", views: 20),
            Post("c", views: 30)));

        var found = await service.DiscoverAsync(Request(2), CancellationToken.None);

        // Scraping bills per item; an uncapped proposal is an uncapped invoice.
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task Discover_ReturnsEmptyWhenTheClientHasNoReferents()
    {
        var service = Build([], new Dictionary<string, Result<ProfileScrapeResult>>());

        // An empty proposal is a legitimate outcome the UI can explain; an exception is not.
        Assert.Empty(await service.DiscoverAsync(Request(10), CancellationToken.None));
    }

    [Fact]
    public async Task Discover_SurvivesOneDeadAccount()
    {
        var scrapes = new Dictionary<string, Result<ProfileScrapeResult>>
        {
            ["@broken"] = Result.Failure<ProfileScrapeResult>(Error.Failure("scrape.failed", "down")),
            ["@ok"] = Scrape("@ok", Post("vivo", views: 1_000))["@ok"]
        };

        var found = await Build(["@broken", "@ok"], scrapes).DiscoverAsync(Request(10), CancellationToken.None);

        Assert.Single(found);
    }

    [Fact]
    public async Task Discover_KeepsPostsWithoutACaptionButTitlesThemFromTheAccount()
    {
        // Unlike an idea, a proposal card is still useful without a caption: the operator judges it by
        // its metrics and thumbnail.
        var service = Build(["@ref"], Scrape("@ref", Post(null, views: 90_000)));

        var found = await service.DiscoverAsync(Request(10), CancellationToken.None);

        Assert.Single(found);
        Assert.Contains("@ref", found[0].Title);
    }

    [Fact]
    public async Task Discover_ExcludesPaidPartnerships()
    {
        var service = Build(["@ref"], Scrape(
            "@ref",
            Post("pauta", views: 900_000, isPaid: true),
            Post("orgánico", views: 100)));

        var found = await service.DiscoverAsync(Request(10), CancellationToken.None);

        // A sponsored post performed because it was paid to; proposing it misleads the operator about
        // what this audience organically rewards.
        Assert.Single(found);
        Assert.Equal("orgánico", found[0].Title);
    }

    [Fact]
    public async Task Discover_ReportsTheNetworkItCameFrom()
    {
        var service = Build(["@ref"], Scrape("@ref", Post("un tema", views: 10)));

        var found = await service.DiscoverAsync(Request(10), CancellationToken.None);

        Assert.Equal("instagram", found[0].NetworkSource);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static ContentDiscoveryRequest Request(int max) => new(Org, Client, [], max);

    private static ReferenceAccountContentDiscovery Build(
        IReadOnlyList<string> referents,
        IReadOnlyDictionary<string, Result<ProfileScrapeResult>> scrapes)
    {
        var accounts = referents
            .Select(h => new ClientAccountSnapshot("instagram", "Instagram", $"https://ig.com/{h}", h, null, null))
            .ToList();

        return new ReferenceAccountContentDiscovery(
            new StubLoader(BrandSnapshot.FromBrandData(null, null, null, null, accounts, null)),
            new StubScraper(scrapes));
    }

    private static ProfilePost Post(
        string? caption,
        long views,
        long likes = 10,
        long comments = 1,
        bool isPaid = false) =>
        new(
            PostId: Guid.NewGuid().ToString(),
            MediaType: "Video",
            LikesCount: likes,
            CommentsCount: comments,
            VideoViewCount: views,
            Timestamp: DateTimeOffset.UtcNow,
            OwnerId: "owner",
            OwnerUsername: "owner",
            IsPaidPartnership: isPaid,
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

    private sealed class StubLoader(BrandSnapshot snapshot) : IClientBrandSnapshotLoader
    {
        public Task<BrandSnapshot> LoadAsync(Guid organizationId, Guid clientId, CancellationToken ct = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class StubScraper(IReadOnlyDictionary<string, Result<ProfileScrapeResult>> scrapes)
        : IProfileScrapeResolver
    {
        public Task<Result<ProfileScrapeResult>> ResolveAsync(ProfileScrapeRequest request, CancellationToken ct = default) =>
            Task.FromResult(
                scrapes.TryGetValue(request.ProfileUrlOrHandle, out var result)
                    ? result
                    : Result.Failure<ProfileScrapeResult>(Error.NotFound("scrape.unknown", "no stub")));
    }
}
