using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Features.Social.ContentGeneration.Discovery;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.ProfileAnalysis;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>WU-4: CD-09, design §6.5. Read-through cache — the only thing standing between "known
/// accounts + TOP N" and a per-candidate scrape bill.</summary>
public sealed class AccountBaselineProviderTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTimeOffset NowUtc = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FreshCachedBaseline_IsReused_NoScrapeTriggered()
    {
        var fresh = new AccountBaseline("tiktok", "@a", 1_000, 5, 20, NowUtc.AddDays(-5));
        var repository = new FakeRepository(fresh);
        var scraper = new CountingScraper();
        var provider = new AccountBaselineProvider(repository, scraper, new FakeCalculator());

        var result = await provider.GetOrComputeAsync(Org, "tiktok", "@a", NowUtc, CancellationToken.None);

        Assert.Equal(fresh, result);
        Assert.Equal(0, scraper.CallCount);
    }

    [Fact]
    public async Task StaleCachedBaseline_TriggersExactlyOneRecomputation()
    {
        var stale = new AccountBaseline("tiktok", "@a", 1_000, 5, 20, NowUtc.AddDays(-45)); // > 30d TTL
        var repository = new FakeRepository(stale);
        var scraper = new CountingScraper();
        var provider = new AccountBaselineProvider(repository, scraper, new FakeCalculator());

        var result = await provider.GetOrComputeAsync(Org, "tiktok", "@a", NowUtc, CancellationToken.None);

        Assert.Equal(1, scraper.CallCount);
        Assert.NotNull(result);
        Assert.NotEqual(stale.ComputedAt, result!.ComputedAt);
        Assert.True(repository.UpsertCallCount >= 1);
    }

    [Fact]
    public async Task NoCachedBaseline_ScrapeFails_ReturnsNull_NeverFabricated()
    {
        var repository = new FakeRepository(cached: null);
        var scraper = new CountingScraper(shouldFail: true);
        var provider = new AccountBaselineProvider(repository, scraper, new FakeCalculator());

        var result = await provider.GetOrComputeAsync(Org, "tiktok", "@unreachable", NowUtc, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, repository.UpsertCallCount);
    }

    [Fact]
    public async Task RepeatedCallsForTheSameAuthor_WithinTtl_NeverExceedOneScrape()
    {
        // Simulates the call-site economics (design §6.5): even if the same author is asked for
        // repeatedly within one run (known account + also appearing in a TOP-N pass), the read-through
        // cache absorbs every call after the first — the mechanism that keeps a 100-candidate set to
        // at most N scrapes.
        var repository = new StatefulFakeRepository();
        var scraper = new CountingScraper();
        var provider = new AccountBaselineProvider(repository, scraper, new FakeCalculator());

        for (var i = 0; i < 100; i++)
        {
            await provider.GetOrComputeAsync(Org, "tiktok", "@a", NowUtc, CancellationToken.None);
        }

        Assert.Equal(1, scraper.CallCount);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeRepository(AccountBaseline? cached) : IAccountBaselineRepository
    {
        public int UpsertCallCount { get; private set; }

        public Task<AccountBaseline?> GetAsync(Guid organizationId, string networkKey, string authorHandle, CancellationToken ct = default) =>
            Task.FromResult(cached);

        public Task UpsertAsync(Guid organizationId, AccountBaseline baseline, CancellationToken ct = default)
        {
            UpsertCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StatefulFakeRepository : IAccountBaselineRepository
    {
        private AccountBaseline? _stored;

        public Task<AccountBaseline?> GetAsync(Guid organizationId, string networkKey, string authorHandle, CancellationToken ct = default) =>
            Task.FromResult(_stored);

        public Task UpsertAsync(Guid organizationId, AccountBaseline baseline, CancellationToken ct = default)
        {
            _stored = baseline;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingScraper(bool shouldFail = false) : IProfileScrapeResolver
    {
        public int CallCount { get; private set; }

        public Task<Result<ProfileScrapeResult>> ResolveAsync(ProfileScrapeRequest request, CancellationToken ct = default)
        {
            CallCount++;

            if (shouldFail)
            {
                return Task.FromResult(Result.Failure<ProfileScrapeResult>(Error.Failure("scrape.unreachable", "unreachable")));
            }

            var profile = new ProfileSnapshot(
                InstagramId: "1", Username: request.ProfileUrlOrHandle, FollowersCount: 1_000, PostsCount: 10,
                ReelsCount: 0, EngagementRate: 3.5, MedianDaysBetweenPosts: 2, ProfilePicUrl: null, Biography: null,
                IsVerified: false, IsBusinessAccount: false, BusinessCategoryName: null);

            var posts = new List<ProfilePost>
            {
                new("p1", "Video", 100, 5, 2_000, DateTimeOffset.UtcNow.AddDays(-1), "1", request.ProfileUrlOrHandle, false, false, []),
            };

            return Task.FromResult(Result.Success(new ProfileScrapeResult(
                profile, posts, [], new ProfileAggregates(10, 1_000, 100, 10, 10, 0), false, [])));
        }
    }

    private sealed class FakeCalculator : IProfileMetricCalculator
    {
        public Result<ProfileStats> Compute(IReadOnlyList<ProfilePost> allPosts, ProfileSnapshot profile) =>
            Result.Success(new ProfileStats(
                EngagementRate: profile.EngagementRate,
                MedianViews: 2_000,
                AvgLikesByWeekday: new Dictionary<int, WeekdayMetric>(),
                AvgCommentsByWeekday: new Dictionary<int, WeekdayMetric>(),
                AvgViewsByWeekday: new Dictionary<int, WeekdayMetric>(),
                ContentTypeMix: new Dictionary<string, double>(),
                PostingCadenceDays: 2,
                OwnerPostCount: allPosts.Count,
                DegradedPostCount: 0));
    }
}
