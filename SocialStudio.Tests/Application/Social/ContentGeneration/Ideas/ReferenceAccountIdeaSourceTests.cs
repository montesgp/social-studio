using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Ideas;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Domain.Social.ProfileAnalysis;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Ideas;

/// <summary>
/// Tests for the reference-accounts idea source: mine the client's referent accounts for the posts
/// that actually performed, and turn those into topics.
///
/// This is the only source so far that carries a REAL signal — view counts — which is what lets the
/// chain keep winners when a batch is trimmed.
/// </summary>
public sealed class ReferenceAccountIdeaSourceTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();

    [Fact]
    public async Task Draw_TurnsWinningPostsIntoIdeas()
    {
        var source = Build(
            referents: ["@ref"],
            scrapes: Scrape("@ref", Post("Cómo migrar de Excel a un CRM", views: 120_000)));

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 5), CancellationToken.None);

        Assert.Single(drawn);
        Assert.Equal("Cómo migrar de Excel a un CRM", drawn[0].Title);
        Assert.Equal("reference-accounts", drawn[0].SourceKey);
    }

    [Fact]
    public async Task Draw_CarriesTheViewCountAsSignal()
    {
        var source = Build(["@ref"], Scrape("@ref", Post("un tema", views: 250_000)));

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 5), CancellationToken.None);

        // The real payoff: a scraped winner outranks anything without a signal when trimming.
        Assert.Equal(250_000d, drawn[0].Signal);
    }

    [Fact]
    public async Task Draw_ReturnsHighestPerformersFirst()
    {
        var source = Build(["@ref"], Scrape(
            "@ref",
            Post("tibio", views: 900),
            Post("ganador", views: 400_000),
            Post("medio", views: 25_000)));

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 3), CancellationToken.None);

        Assert.Equal(["ganador", "medio", "tibio"], drawn.Select(i => i.Title).ToArray());
    }

    [Fact]
    public async Task Draw_FallsBackToEngagementWhenAPostHasNoViews()
    {
        // Images and carousels carry no videoViewCount. Treating them as zero would bury every
        // non-video winner beneath the weakest reel.
        var source = Build(["@ref"], Scrape(
            "@ref",
            Post("reel flojo", views: 500),
            Post("carrusel fuerte", views: null, likes: 9_000, comments: 400)));

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 2), CancellationToken.None);

        Assert.Equal("carrusel fuerte", drawn[0].Title);
    }

    [Fact]
    public async Task Draw_ReturnsNothingWhenTheClientHasNoReferents()
    {
        var source = Build(referents: [], scrapes: new Dictionary<string, Result<ProfileScrapeResult>>());

        // Degradation, not failure: the chain moves on to the next source.
        Assert.Empty(await source.DrawAsync(new ContentIdeaRequest(Org, Client, 5), CancellationToken.None));
    }

    [Fact]
    public async Task Draw_SkipsPostsWithoutACaption()
    {
        var source = Build(["@ref"], Scrape(
            "@ref",
            Post(null, views: 900_000),
            Post("con texto", views: 1_000)));

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 5), CancellationToken.None);

        // A caption-less post gives no topic to write about, however well it performed.
        Assert.Single(drawn);
        Assert.Equal("con texto", drawn[0].Title);
    }

    [Fact]
    public async Task Draw_KeepsGoingWhenOneReferentFailsToScrape()
    {
        var scrapes = new Dictionary<string, Result<ProfileScrapeResult>>
        {
            ["@broken"] = Result.Failure<ProfileScrapeResult>(Error.Failure("scrape.failed", "actor down")),
            ["@ok"] = Scrape("@ok", Post("sobrevivió", views: 5_000))["@ok"]
        };

        var source = Build(["@broken", "@ok"], scrapes);

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 5), CancellationToken.None);

        // One dead account must not cost the operator every other reference.
        Assert.Single(drawn);
        Assert.Equal("sobrevivió", drawn[0].Title);
    }

    [Fact]
    public async Task Draw_ThrowsOnlyWhenEveryReferentFails()
    {
        var scrapes = new Dictionary<string, Result<ProfileScrapeResult>>
        {
            ["@a"] = Result.Failure<ProfileScrapeResult>(Error.Failure("scrape.failed", "down")),
            ["@b"] = Result.Failure<ProfileScrapeResult>(Error.Failure("scrape.failed", "down"))
        };

        var source = Build(["@a", "@b"], scrapes);

        // Total provider outage is a failure the chain must report, not a silent empty result that
        // reads as "this client has no references".
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.DrawAsync(new ContentIdeaRequest(Org, Client, 5), CancellationToken.None));
    }

    [Fact]
    public async Task Draw_TrimsToTheRequestedCount()
    {
        var source = Build(["@ref"], Scrape(
            "@ref",
            Post("a", views: 10),
            Post("b", views: 20),
            Post("c", views: 30)));

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 2), CancellationToken.None);

        Assert.Equal(2, drawn.Count);
    }

    [Fact]
    public async Task Draw_ReferencesTheAccountItCameFrom()
    {
        var source = Build(["@ref"], Scrape("@ref", Post("un tema", views: 100)));

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 1), CancellationToken.None);

        // Provenance so the operator can see which reference inspired the piece.
        Assert.Contains("@ref", drawn[0].Reference);
    }

    [Fact]
    public async Task Draw_SkipsPaidPartnershipPosts()
    {
        var source = Build(["@ref"], Scrape(
            "@ref",
            Post("publicidad", views: 800_000, isPaid: true),
            Post("orgánico", views: 1_000)));

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 5), CancellationToken.None);

        // A sponsored post performs because it was paid to. Copying it teaches the wrong lesson.
        Assert.Single(drawn);
        Assert.Equal("orgánico", drawn[0].Title);
    }

    [Fact]
    public async Task Draw_RequestsNothingWhenCountIsZero()
    {
        var scraper = new FakeScraper(Scrape("@ref", Post("x", views: 1)));
        var source = new ReferenceAccountIdeaSource(Loader(["@ref"]), scraper);

        var drawn = await source.DrawAsync(new ContentIdeaRequest(Org, Client, 0), CancellationToken.None);

        Assert.Empty(drawn);
        Assert.False(scraper.WasCalled);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static ReferenceAccountIdeaSource Build(
        IReadOnlyList<string> referents,
        IReadOnlyDictionary<string, Result<ProfileScrapeResult>> scrapes) =>
        new(Loader(referents), new FakeScraper(scrapes));

    private static IClientBrandSnapshotLoader Loader(IReadOnlyList<string> handles)
    {
        var accounts = handles
            .Select(h => new ClientAccountSnapshot("instagram", "Instagram", $"https://ig.com/{h}", h, null, null))
            .ToList();

        return new FakeLoader(BrandSnapshot.FromBrandData(null, null, null, null, accounts, null));
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

    private sealed class FakeLoader(BrandSnapshot snapshot) : IClientBrandSnapshotLoader
    {
        public Task<BrandSnapshot> LoadAsync(Guid organizationId, Guid clientId, CancellationToken ct = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class FakeScraper(IReadOnlyDictionary<string, Result<ProfileScrapeResult>> scrapes)
        : IProfileScrapeResolver
    {
        public bool WasCalled { get; private set; }

        public Task<Result<ProfileScrapeResult>> ResolveAsync(ProfileScrapeRequest request, CancellationToken ct = default)
        {
            WasCalled = true;

            return Task.FromResult(
                scrapes.TryGetValue(request.ProfileUrlOrHandle, out var result)
                    ? result
                    : Result.Failure<ProfileScrapeResult>(Error.NotFound("scrape.unknown", "no stub")));
        }
    }
}
