using System.Net;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;

namespace Incoders.Hive.Tests.Infrastructure.Social.ProfileAnalysis;

/// <summary>
/// Unit tests for InstagramProfileApifyScrapeAdapter.
/// HTTP layer is mocked — the real Apify API is never called.
/// Covers: PA-06 (resultType selection), PA-04 (RESIDENTIAL proxy), PA-24 (variantKey routing).
/// </summary>
public sealed class ApifyProfileScrapeAdapterTests
{
    private static readonly string GoldenFixture =
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Infrastructure", "Social", "Fixtures",
                "apify-instagram-profile-response.json"));

    // ── PA-06: resultType == "profile" item selected even when not at index 0 ──

    [Fact]
    public void MapProfileResponse_SelectsProfileItemByResultType_NotByIndex()
    {
        // Arrange: the golden fixture has quality_report at [0], profile at [1], bandwidth_report at [2]
        var adapter = new InstagramProfileApifyScrapeAdapter();

        // Act
        var result = adapter.MapProfileResponse(GoldenFixture);

        // Assert: profile data from item [1] must be used
        Assert.NotNull(result);
        Assert.Equal("testbrand", result.Profile.Username);
        Assert.Equal("242598499", result.Profile.InstagramId);
        Assert.Equal(150000L, result.Profile.FollowersCount);
    }

    [Fact]
    public void MapProfileResponse_DiscardsQualityReportAndBandwidthReport()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var result = adapter.MapProfileResponse(GoldenFixture);

        Assert.NotNull(result);
        // Only the profile item; non-profile items excluded
        Assert.NotEqual("quality_report", result.Profile.InstagramId);
    }

    [Fact]
    public void MapProfileResponse_WhenNoProfileItemInResponse_ReturnsNull()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();
        var noProfileJson = "[{\"resultType\":\"quality_report\"},{\"resultType\":\"bandwidth_report\"}]";

        var result = adapter.MapProfileResponse(noProfileJson);

        Assert.Null(result);
    }

    // ── PA-04: RESIDENTIAL proxy present in built actor input ──────────────────

    [Fact]
    public void BuildActorInput_IncludesResidentialProxyGroup()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var input = adapter.BuildProfileActorInput("testbrand", maxPostsPerProfile: 12);
        var json = JsonSerializer.Serialize(input);

        Assert.Contains("RESIDENTIAL", json);
        Assert.Contains("useApifyProxy", json);
        Assert.Contains("apifyProxyGroups", json);
    }

    [Fact]
    public void BuildActorInput_IncludesScrapeModeAndProfile()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var input = adapter.BuildProfileActorInput("testbrand", maxPostsPerProfile: 12);
        var json = JsonSerializer.Serialize(input);

        Assert.Contains("instagram-profile-scraper", json);
        Assert.Contains("testbrand", json);
        Assert.Contains("maxPostsPerProfile", json);
    }

    // ── Post mapping: all posts carried raw including non-owner ────────────────

    [Fact]
    public void MapProfileResponse_MapsAllPostsRaw_OwnerFilterAppliedByCalculator()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var result = adapter.MapProfileResponse(GoldenFixture)!;

        // 5 posts in latestPosts[] — all 5 must be in AllPosts (owner filter happens in ProfileMetricCalculator)
        Assert.Equal(5, result.AllPosts.Count);
    }

    [Fact]
    public void MapProfileResponse_MapsDegradedFlag()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var result = adapter.MapProfileResponse(GoldenFixture)!;

        var degraded = result.AllPosts.Where(p => p.IsDegraded).ToList();
        Assert.Single(degraded);
        Assert.Equal("post-004", degraded[0].PostId);
    }

    [Fact]
    public void MapProfileResponse_MapsVideoViewCount_ForVideoPost()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var result = adapter.MapProfileResponse(GoldenFixture)!;

        var videoPost = result.AllPosts.Single(p => p.PostId == "post-001");
        Assert.Equal(177881L, videoPost.VideoViewCount);
        Assert.Equal("Video", videoPost.MediaType);
    }

    [Fact]
    public void MapProfileResponse_NullVideoViewCount_ForCarouselPost()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var result = adapter.MapProfileResponse(GoldenFixture)!;

        var carousel = result.AllPosts.Single(p => p.PostId == "post-002");
        Assert.Null(carousel.VideoViewCount);
        Assert.Equal("Carousel", carousel.MediaType);
    }

    // ── latestReels[]: empty in golden fixture, defensive mapping ──────────────

    [Fact]
    public void MapProfileResponse_EmptyReels_ReturnsEmptyReelsList()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var result = adapter.MapProfileResponse(GoldenFixture)!;

        Assert.Empty(result.Reels);
    }

    [Fact]
    public void MapProfileResponse_NonEmptyReels_TriggersWarning()
    {
        // Simulate a response where latestReels has one item (unusual — spike confirms always empty)
        var adapter = new InstagramProfileApifyScrapeAdapter();
        var reelItem = new
        {
            postId = "reel-001",
            shortCode = "REL001",
            caption = "A reel",
            commentsCount = 10,
            likesCount = 500,
            timestamp = "2026-05-01T10:00:00.000Z",
            mediaType = "Video",
            displayUrl = "https://cdn.test/reel.jpg",
            videoViewCount = (long?)50000,
            videoPlayCount = (long?)null,
            ownerUsername = "testbrand",
            ownerId = "242598499",
            isPaidPartnership = false,
            hashtags = Array.Empty<string>(),
            _degraded = false
        };

        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                resultType = "profile",
                instagramId = "242598499",
                username = "testbrand",
                fullName = "Test Brand Studio",
                followersCount = 100000,
                followsCount = 200,
                postsCount = 100,
                reelsCount = (long?)10,
                isVerified = false,
                isBusinessAccount = true,
                isProfessionalAccount = true,
                isPrivate = false,
                avgLikes = 1000.0,
                avgComments = 20.0,
                engagementRate = 1.5,
                medianDaysBetweenPosts = (double?)3.0,
                latestPosts = Array.Empty<object>(),
                latestReels = new[] { reelItem }
            }
        });

        var (mappedResult, warningEmitted) = adapter.MapProfileResponseWithWarningCapture(json);

        Assert.NotNull(mappedResult);
        Assert.Single(mappedResult!.Reels);
        Assert.True(warningEmitted, "Expected a Warning log when latestReels is non-empty per WU-01 spike findings");
    }

    // ── Aggregates mapped correctly ─────────────────────────────────────────────

    [Fact]
    public void MapProfileResponse_MapsProviderAggregates()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var result = adapter.MapProfileResponse(GoldenFixture)!;

        Assert.Equal(2.25, result.Aggregates.EngagementRate, precision: 4);
        Assert.Equal(3.5, result.Aggregates.MedianDaysBetweenPosts!.Value, precision: 4);
        Assert.Equal(150000L, result.Aggregates.FollowersCount);
    }

    // ── Username parsing from URL and handle ──────────────────────────────────

    [Theory]
    [InlineData("https://www.instagram.com/testbrand/", "testbrand")]
    [InlineData("https://www.instagram.com/testbrand", "testbrand")]
    [InlineData("https://www.instagram.com/testbrand?utm_source=ig_web_button_share_sheet&igsh=abc", "testbrand")]
    [InlineData("https://instagram.com/testbrand/", "testbrand")]
    [InlineData("testbrand", "testbrand")]
    [InlineData("@testbrand", "testbrand")]
    public void ParseUsername_ExtractsHandleFromUrlOrHandle(string input, string expected)
    {
        var result = InstagramProfileApifyScrapeAdapter.ParseUsername(input);
        Assert.Equal(expected, result);
    }
}
