using System.Net;
using System.Text;
using System.Text.Json;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Social.Providers.Apify;

namespace SocialStudio.Tests.Infrastructure.Social.ProfileAnalysis;

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

    // ── PA-04: actor-specific inputs keep Apify contracts valid ───────────────

    [Fact]
    public void BuildActorInput_ForGetLeadsActor_UsesProfilesAndProxyTier()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var input = adapter.BuildProfileActorInput(
            "get-leads/all-in-one-instagram-scraper",
            "testbrand",
            maxPostsPerProfile: 12);
        var json = JsonSerializer.Serialize(input);

        Assert.Contains("\"profiles\"", json);
        Assert.Contains("\"testbrand\"", json);
        Assert.Contains("\"scrapeMode\":\"instagram-profile-scraper\"", json);
        Assert.Contains("\"maxPostsPerProfile\":12", json);
        Assert.Contains("\"proxyTier\":\"residential\"", json);
        Assert.DoesNotContain("\"proxy\"", json);
        Assert.DoesNotContain("\"usernames\"", json);
    }

    [Fact]
    public void BuildActorInput_ForOfficialApifyProfileActor_UsesUsernames()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();

        var input = adapter.BuildProfileActorInput(
            "apify/instagram-profile-scraper",
            "testbrand",
            maxPostsPerProfile: 12);
        var json = JsonSerializer.Serialize(input);

        Assert.Contains("\"usernames\"", json);
        Assert.Contains("\"testbrand\"", json);
        Assert.DoesNotContain("\"profiles\"", json);
        Assert.DoesNotContain("\"scrapeMode\"", json);
        Assert.DoesNotContain("\"proxy\"", json);
    }

    [Fact]
    public void MapProfileResponse_MapsOfficialApifyProfileObjectWithoutResultType()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                inputUrl = "https://www.instagram.com/testbrand",
                id = "242598499",
                username = "testbrand",
                followersCount = 150000,
                followsCount = 200,
                postsCount = 100,
                verified = true,
                isBusinessAccount = true,
                businessCategoryName = "Marketing",
                latestPosts = new[]
                {
                    new
                    {
                        shortCode = "ABC123",
                        mediaType = "Image",
                        likesCount = 1200,
                        commentsCount = 30,
                        timestamp = "2026-05-01T10:00:00.000Z",
                        ownerUsername = "testbrand",
                        hashtags = new[] { "marketing" }
                    }
                }
            }
        });

        var result = adapter.MapProfileResponse(json);

        Assert.NotNull(result);
        Assert.Equal("testbrand", result!.Profile.Username);
        Assert.Equal("242598499", result.Profile.InstagramId);
        Assert.True(result.Profile.IsVerified);
        var post = Assert.Single(result.AllPosts);
        Assert.Equal("ABC123", post.PostId);
        Assert.Equal("242598499", post.OwnerId);
    }

    [Fact]
    public void MapProfileResponse_DoesNotMapInputValidationErrorAsProfile()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                resultType = "input_validation_error",
                message = "Unknown input field proxy",
                hint = "Use proxyTier instead."
            }
        });

        var result = adapter.MapProfileResponse(json);

        Assert.Null(result);
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

    // ── CAG-PA-01: childrenCount mapping (carousel slides) ────────────────────

    [Fact]
    public void MapSinglePost_CarouselWithChildrenCount_SetsCarouselChildrenCount()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                resultType = "profile",
                instagramId = "999",
                username = "testbrand",
                followersCount = 1000,
                postsCount = 10,
                latestPosts = new[]
                {
                    new
                    {
                        postId = "carousel-001",
                        mediaType = "Carousel",
                        likesCount = 100,
                        commentsCount = 5,
                        timestamp = "2026-05-01T10:00:00Z",
                        ownerUsername = "testbrand",
                        ownerId = "999",
                        isPaidPartnership = false,
                        hashtags = Array.Empty<string>(),
                        childrenCount = 6
                    }
                }
            }
        });

        var result = adapter.MapProfileResponse(json)!;

        var post = Assert.Single(result.AllPosts);
        Assert.Equal(6, post.CarouselChildrenCount);
    }

    [Fact]
    public void MapSinglePost_PostWithoutChildrenCount_CarouselChildrenCountIsNull()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                resultType = "profile",
                instagramId = "999",
                username = "testbrand",
                followersCount = 1000,
                postsCount = 10,
                latestPosts = new[]
                {
                    new
                    {
                        postId = "carousel-002",
                        mediaType = "Carousel",
                        likesCount = 100,
                        commentsCount = 5,
                        timestamp = "2026-05-01T10:00:00Z",
                        ownerUsername = "testbrand",
                        ownerId = "999",
                        isPaidPartnership = false,
                        hashtags = Array.Empty<string>()
                        // no childrenCount field
                    }
                }
            }
        });

        var result = adapter.MapProfileResponse(json)!;

        var post = Assert.Single(result.AllPosts);
        Assert.Null(post.CarouselChildrenCount);
    }

    [Fact]
    public void MapSinglePost_NonCarouselPost_CarouselChildrenCountIsNull()
    {
        var adapter = new InstagramProfileApifyScrapeAdapter();
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                resultType = "profile",
                instagramId = "999",
                username = "testbrand",
                followersCount = 1000,
                postsCount = 10,
                latestPosts = new[]
                {
                    new
                    {
                        postId = "video-001",
                        mediaType = "Video",
                        likesCount = 500,
                        commentsCount = 10,
                        timestamp = "2026-05-01T10:00:00Z",
                        ownerUsername = "testbrand",
                        ownerId = "999",
                        isPaidPartnership = false,
                        hashtags = Array.Empty<string>(),
                        videoViewCount = 10000,
                        childrenCount = 3  // field present but irrelevant for non-carousel
                    }
                }
            }
        });

        var result = adapter.MapProfileResponse(json)!;

        var post = Assert.Single(result.AllPosts);
        Assert.Equal("Video", post.MediaType);
        // For non-carousel posts the adapter still passes the field through;
        // the caller (ProfileMetricCalculator) uses it only for Carousel posts.
        // The test here confirms the adapter does NOT crash and correctly reads the value.
        Assert.Null(post.CarouselChildrenCount); // not carousel → null by design
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
