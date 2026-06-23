using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Domain.Social.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ProfileAnalyzer;

/// <summary>
/// Unit tests for ProfileMetricCalculator (PA-07, PA-08, PA-09, PA-12).
/// All pure logic — no I/O, no external deps.
/// </summary>
public sealed class ProfileMetricCalculatorTests
{
    private static readonly ProfileSnapshot DefaultProfile = new(
        InstagramId: "12345",
        Username: "testbrand",
        FollowersCount: 10_000,
        PostsCount: 50,
        ReelsCount: 10,
        EngagementRate: 3.5,
        MedianDaysBetweenPosts: 3.0,
        ProfilePicUrl: null,
        Biography: null,
        IsVerified: false,
        IsBusinessAccount: true,
        BusinessCategoryName: "Brand");

    private readonly ProfileMetricCalculator _calculator = new();

    // ── PA-07: Owner filter ───────────────────────────────────────────────────

    [Fact]
    public void Compute_ExcludesPostsWhereOwnerIdDiffersFromProfileInstagramId()
    {
        var posts = new[]
        {
            MakePost("p1", "Video", ownerId: "12345", videoViewCount: 1000),  // owner
            MakePost("p2", "Image", ownerId: "99999", videoViewCount: null),  // tagged — excluded
            MakePost("p3", "Image", ownerId: "12345", videoViewCount: null),  // owner
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        // Only 2 owner posts used
        Assert.Equal(2, result.Value.OwnerPostCount);
    }

    // ── PA-08: Degraded posts excluded; IsPartial stats still computed ────────

    [Fact]
    public void Compute_ExcludesDegradedPosts_AndCountsThemInDegradedPostCount()
    {
        var posts = new[]
        {
            MakePost("p1", "Image", ownerId: "12345", isDegraded: false),
            MakePost("p2", "Image", ownerId: "12345", isDegraded: true),  // excluded
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.OwnerPostCount);
        Assert.Equal(1, result.Value.DegradedPostCount);
    }

    // ── PA-08: All owner posts degraded → NoUsableOwnerPosts failure ──────────

    [Fact]
    public void Compute_AllOwnerPostsDegraded_ReturnsNoUsableOwnerPostsError()
    {
        var posts = new[]
        {
            MakePost("p1", "Image", ownerId: "12345", isDegraded: true),
            MakePost("p2", "Video", ownerId: "12345", isDegraded: true),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.no_usable_owner_posts", result.Error.Code);
    }

    // ── No posts at all → NoUsableOwnerPosts failure ──────────────────────────

    [Fact]
    public void Compute_NoOwnerPosts_ReturnsNoUsableOwnerPostsError()
    {
        var posts = new[]
        {
            MakePost("p1", "Image", ownerId: "99999"), // tagged, not owner
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.no_usable_owner_posts", result.Error.Code);
    }

    // ── PA-09: Median views over Video/Reel posts only ────────────────────────

    [Fact]
    public void Compute_MedianViews_CalculatedFromVideoAndReelPostsOnly()
    {
        var posts = new[]
        {
            MakePost("p1", "Video", ownerId: "12345", videoViewCount: 100),
            MakePost("p2", "Video", ownerId: "12345", videoViewCount: 300),
            MakePost("p3", "Video", ownerId: "12345", videoViewCount: 200),
            MakePost("p4", "Image", ownerId: "12345", videoViewCount: null), // excluded from median
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        // Sorted: [100, 200, 300] → median = 200
        Assert.Equal(200.0, result.Value.MedianViews);
    }

    [Fact]
    public void Compute_MedianViews_NullWhenNoVideoOrReelPosts()
    {
        var posts = new[]
        {
            MakePost("p1", "Image", ownerId: "12345"),
            MakePost("p2", "Carousel", ownerId: "12345"),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.MedianViews);
    }

    [Fact]
    public void Compute_MedianViews_EvenNumberOfVideoPostsUsesAverageOfMiddleTwo()
    {
        var posts = new[]
        {
            MakePost("p1", "Video", ownerId: "12345", videoViewCount: 100),
            MakePost("p2", "Video", ownerId: "12345", videoViewCount: 200),
            MakePost("p3", "Video", ownerId: "12345", videoViewCount: 300),
            MakePost("p4", "Video", ownerId: "12345", videoViewCount: 400),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        // Sorted: [100, 200, 300, 400] → median = (200+300)/2 = 250
        Assert.Equal(250.0, result.Value.MedianViews);
    }

    // ── PA-09: Carousel excluded from median even if videoViewCount != null ───

    [Fact]
    public void Compute_CarouselExcludedFromMedianEvenIfVideoViewCountIsNonNull()
    {
        var posts = new[]
        {
            MakePost("p1", "Video", ownerId: "12345", videoViewCount: 500),
            MakePost("p2", "Carousel", ownerId: "12345", videoViewCount: 9999), // must be excluded
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        // Only the Video post contributes: median = 500
        Assert.Equal(500.0, result.Value.MedianViews);
    }

    // ── PA-09: Reel counts as Video for median ────────────────────────────────

    [Fact]
    public void Compute_ReelCountsAsVideoForMedianViews()
    {
        var posts = new[]
        {
            MakePost("p1", "Reel", ownerId: "12345", videoViewCount: 600),
            MakePost("p2", "Video", ownerId: "12345", videoViewCount: 400),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        // Sorted: [400, 600] → median = (400+600)/2 = 500
        Assert.Equal(500.0, result.Value.MedianViews);
    }

    // ── PA-12: Weekday averages from UTC-normalised timestamps ────────────────

    [Fact]
    public void Compute_WeekdayBuckets_ComputedFromUtcNormalisedTimestamps()
    {
        // Monday UTC = DayOfWeek.Monday (1) → bucket 0 in ISO-8601 (Mon=0)
        var monday = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero); // Known Monday
        var tuesday = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero); // Known Tuesday

        var posts = new[]
        {
            MakePost("p1", "Image", ownerId: "12345", likesCount: 100, commentsCount: 10, timestamp: monday),
            MakePost("p2", "Image", ownerId: "12345", likesCount: 200, commentsCount: 20, timestamp: monday),
            MakePost("p3", "Image", ownerId: "12345", likesCount: 300, commentsCount: 30, timestamp: tuesday),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);

        // ISO-8601: Monday = 0
        Assert.True(result.Value.AvgLikesByWeekday.ContainsKey(0), "Monday bucket must exist");
        Assert.Equal(150.0, result.Value.AvgLikesByWeekday[0].AvgLikes); // (100+200)/2
        Assert.Equal(15.0, result.Value.AvgLikesByWeekday[0].AvgComments); // (10+20)/2

        // ISO-8601: Tuesday = 1
        Assert.True(result.Value.AvgLikesByWeekday.ContainsKey(1), "Tuesday bucket must exist");
        Assert.Equal(300.0, result.Value.AvgLikesByWeekday[1].AvgLikes);
    }

    [Fact]
    public void Compute_NonUtcTimestamp_NormalisedToUtcBeforeWeekdayBucketAssignment()
    {
        // 2026-06-01 23:00 UTC+3 = 2026-06-01 20:00 UTC (still Monday)
        // vs 2026-06-02 01:00 UTC+3 = 2026-06-01 22:00 UTC (still Monday in UTC)
        var mondayEveningLocal = new DateTimeOffset(2026, 6, 2, 1, 0, 0, TimeSpan.FromHours(3));
        // UTC equivalent: 2026-06-01 22:00 UTC = Monday

        var posts = new[]
        {
            MakePost("p1", "Image", ownerId: "12345", likesCount: 100, timestamp: mondayEveningLocal),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        // UTC weekday is Monday (0) — NOT Tuesday (1)
        Assert.True(result.Value.AvgLikesByWeekday.ContainsKey(0),
            "UTC normalisation must place the post in Monday (0) bucket, not Tuesday (1).");
        Assert.False(result.Value.AvgLikesByWeekday.ContainsKey(1),
            "Tuesday bucket must be absent after UTC normalisation.");
    }

    // ── Content type mix ──────────────────────────────────────────────────────

    [Fact]
    public void Compute_ContentTypeMix_PercentagesAddToOneHundred()
    {
        var posts = new[]
        {
            MakePost("p1", "Video", ownerId: "12345"),
            MakePost("p2", "Video", ownerId: "12345"),
            MakePost("p3", "Image", ownerId: "12345"),
            MakePost("p4", "Carousel", ownerId: "12345"),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        var total = result.Value.ContentTypeMix.Values.Sum();
        Assert.Equal(100.0, total, precision: 5);
    }

    // ── Posting cadence ───────────────────────────────────────────────────────

    [Fact]
    public void Compute_PostingCadence_AverageDaysBetweenConsecutivePosts()
    {
        // 3 posts: Day 1, Day 4, Day 7 → gaps = [3, 3] → cadence = 3 days
        var base1 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var base2 = new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero);
        var base3 = new DateTimeOffset(2026, 5, 7, 0, 0, 0, TimeSpan.Zero);

        var posts = new[]
        {
            MakePost("p1", "Image", ownerId: "12345", timestamp: base1),
            MakePost("p2", "Image", ownerId: "12345", timestamp: base2),
            MakePost("p3", "Image", ownerId: "12345", timestamp: base3),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        Assert.Equal(3.0, result.Value.PostingCadenceDays, precision: 3);
    }

    // ── CAG-PA-02: AverageSlidesPerCarousel computation ──────────────────────

    [Fact]
    public void Compute_AverageSlidesPerCarousel_MeanOfNonNullChildCounts()
    {
        // Two carousel posts with childrenCount 4 and 6 → mean = 5.0, rounded to 1 d.p.
        var posts = new[]
        {
            MakePost("p1", "Carousel", ownerId: "12345", carouselChildrenCount: 4),
            MakePost("p2", "Carousel", ownerId: "12345", carouselChildrenCount: 6),
            MakePost("p3", "Image", ownerId: "12345"),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        Assert.Equal(5.0, result.Value.AverageSlidesPerCarousel);
    }

    [Fact]
    public void Compute_AverageSlidesPerCarousel_NullCountsExcludedFromBothNumeratorAndDenominator()
    {
        // Three carousels: only two have childrenCount set → null excluded from denominator
        var posts = new[]
        {
            MakePost("p1", "Carousel", ownerId: "12345", carouselChildrenCount: 3),
            MakePost("p2", "Carousel", ownerId: "12345", carouselChildrenCount: null),  // excluded
            MakePost("p3", "Carousel", ownerId: "12345", carouselChildrenCount: 7),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        // mean(3, 7) = 5.0 (not mean(3, 0, 7))
        Assert.Equal(5.0, result.Value.AverageSlidesPerCarousel);
    }

    [Fact]
    public void Compute_AverageSlidesPerCarousel_AllNullOrNoCarousels_ReturnsNull()
    {
        // No carousel posts → null
        var posts = new[]
        {
            MakePost("p1", "Image", ownerId: "12345"),
            MakePost("p2", "Video", ownerId: "12345", videoViewCount: 1000),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.AverageSlidesPerCarousel);
    }

    [Fact]
    public void Compute_AverageSlidesPerCarousel_RoundedToOneDecimalPlace()
    {
        // 3 + 4 + 5 = 12, mean = 4.0 exactly → 4.0
        // Use non-integer result: 3 + 4 = 7, mean = 3.5 → 3.5
        var posts = new[]
        {
            MakePost("p1", "Carousel", ownerId: "12345", carouselChildrenCount: 3),
            MakePost("p2", "Carousel", ownerId: "12345", carouselChildrenCount: 4),
        };

        var result = _calculator.Compute(posts, DefaultProfile);

        Assert.True(result.IsSuccess);
        Assert.Equal(3.5, result.Value.AverageSlidesPerCarousel);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProfilePost MakePost(
        string postId,
        string mediaType = "Image",
        string ownerId = "12345",
        long? videoViewCount = null,
        bool isDegraded = false,
        long likesCount = 50,
        long commentsCount = 5,
        DateTimeOffset? timestamp = null,
        int? carouselChildrenCount = null) =>
        new(
            PostId: postId,
            MediaType: mediaType,
            LikesCount: likesCount,
            CommentsCount: commentsCount,
            VideoViewCount: videoViewCount,
            Timestamp: timestamp ?? DateTimeOffset.UtcNow,
            OwnerId: ownerId,
            OwnerUsername: "testbrand",
            IsPaidPartnership: false,
            IsDegraded: isDegraded,
            Hashtags: [],
            CarouselChildrenCount: carouselChildrenCount);
}
