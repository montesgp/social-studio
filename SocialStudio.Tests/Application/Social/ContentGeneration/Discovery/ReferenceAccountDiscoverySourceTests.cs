using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Discovery;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Domain.Social.ProfileAnalysis;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// WU-3: <see cref="ReferenceAccountDiscoverySource"/> — moved from
/// <c>ReferenceAccountContentDiscovery</c>, now an <see cref="IContentDiscoverySource"/>,
/// <c>Order: 1</c>, <c>Supports("references")</c>. Metrics are nullable now (D2) — the
/// <c>?? 0</c> zero-fill is gone.
/// </summary>
public sealed class ReferenceAccountDiscoverySourceTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();

    [Fact]
    public void SourceKey_And_Order_And_Supports()
    {
        var source = Build([], new Dictionary<string, Result<ProfileScrapeResult>>());

        Assert.Equal("references", source.SourceKey);
        Assert.Equal(1, source.Order);
        Assert.True(source.Supports("references"));
        Assert.False(source.Supports("niche"));
        Assert.False(source.Supports("own"));
    }

    [Fact]
    public async Task Discover_ReturnsRealMetrics_AsNullableLongs()
    {
        var source = Build(["@ref"], Scrape("@ref", Post("Un reel", views: 120_000, likes: 4_000, comments: 210)));

        var result = await source.DiscoverAsync(Request(10), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        var item = Assert.Single(result.Candidates);
        Assert.Equal(120_000, item.Views);
        Assert.Equal(4_000, item.Likes);
        Assert.Equal(210, item.Comments);
        Assert.Equal("@ref", item.SourceHandle);
        Assert.Equal("references", item.SourceKey);
    }

    [Fact]
    public async Task Discover_DoesNotZeroFillAMissingViewCount()
    {
        var source = Build(["@ref"], Scrape("@ref", Post("Sin vistas", views: null)));

        var result = await source.DiscoverAsync(Request(10), CancellationToken.None);

        // The `?? 0` zero-fill this change deletes: unavailable stays null, never 0.
        Assert.Null(Assert.Single(result.Candidates).Views);
    }

    [Fact]
    public async Task Discover_RanksTheStrongestFirst()
    {
        var source = Build(["@ref"], Scrape(
            "@ref",
            Post("flojo", views: 900),
            Post("ganador", views: 500_000),
            Post("medio", views: 30_000)));

        var result = await source.DiscoverAsync(Request(10), CancellationToken.None);

        Assert.Equal(["ganador", "medio", "flojo"], result.Candidates.Select(c => c.Title).ToArray());
    }

    [Fact]
    public async Task Discover_ReturnsRanWithEmptyCandidates_WhenTheClientHasNoReferents()
    {
        var source = Build([], new Dictionary<string, Result<ProfileScrapeResult>>());

        var result = await source.DiscoverAsync(Request(10), CancellationToken.None);

        Assert.Equal(SourceOutcome.Ran, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Discover_SurvivesOneDeadAccount()
    {
        var scrapes = new Dictionary<string, Result<ProfileScrapeResult>>
        {
            ["@broken"] = Result.Failure<ProfileScrapeResult>(Error.Failure("scrape.failed", "down")),
            ["@ok"] = Scrape("@ok", Post("vivo", views: 1_000))["@ok"]
        };

        var result = await Build(["@broken", "@ok"], scrapes).DiscoverAsync(Request(10), CancellationToken.None);

        Assert.Single(result.Candidates);
    }

    [Fact]
    public async Task Discover_ExcludesPaidPartnerships()
    {
        var source = Build(["@ref"], Scrape(
            "@ref",
            Post("pauta", views: 900_000, isPaid: true),
            Post("orgánico", views: 100)));

        var result = await source.DiscoverAsync(Request(10), CancellationToken.None);

        Assert.Single(result.Candidates);
        Assert.Equal("orgánico", result.Candidates[0].Title);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static ContentDiscoveryRequest Request(int max) => new(Org, Client, [], max, "references");

    private static ReferenceAccountDiscoverySource Build(
        IReadOnlyList<string> referents,
        IReadOnlyDictionary<string, Result<ProfileScrapeResult>> scrapes)
    {
        var accounts = referents
            .Select(h => new ClientAccountSnapshot("instagram", "Instagram", $"https://ig.com/{h}", h, null, null))
            .ToList();

        return new ReferenceAccountDiscoverySource(
            new StubLoader(BrandSnapshot.FromBrandData(null, null, null, null, accounts, null)),
            new StubScraper(scrapes));
    }

    private static ProfilePost Post(
        string? caption,
        long? views,
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
