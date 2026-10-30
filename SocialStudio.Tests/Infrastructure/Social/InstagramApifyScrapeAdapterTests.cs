using System.Text.Json;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Infrastructure.Social.Providers.Apify;

namespace SocialStudio.Tests.Infrastructure.Social;

/// <summary>
/// Unit tests for InstagramApifyScrapeAdapter (actor shu8hvrXbJbY3Eb9W).
/// Field mappings verified against the golden fixture at apify-instagram-item.json (obs 426).
/// </summary>
public sealed class InstagramApifyScrapeAdapterTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Social", "Fixtures");

    private static SocialContentRequest BuildRequest(string url = "https://www.instagram.com/p/DPwJzEVDbwC/") =>
        new(Guid.NewGuid(), url, url, "instagram", "social-extraction");

    // ─── PlatformKey ─────────────────────────────────────────────────────────

    [Fact]
    public void PlatformKey_IsInstagram()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        Assert.Equal("instagram", adapter.PlatformKey);
    }

    // ─── BuildActorInput shape ────────────────────────────────────────────────

    [Fact]
    public void BuildActorInput_ProducesDirectUrlsShape()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();

        var input = adapter.BuildActorInput(request);
        var json = JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // resultsType must be "posts"
        Assert.True(root.TryGetProperty("resultsType", out var resultsType));
        Assert.Equal("posts", resultsType.GetString());

        // directUrls must be an array containing the normalized URL
        Assert.True(root.TryGetProperty("directUrls", out var directUrls));
        Assert.Equal(JsonValueKind.Array, directUrls.ValueKind);
        Assert.Equal(request.NormalizedUrl, directUrls[0].GetString());

        // resultsLimit = 1
        Assert.True(root.TryGetProperty("resultsLimit", out var limit));
        Assert.Equal(1, limit.GetInt32());

        // addParentData = false
        Assert.True(root.TryGetProperty("addParentData", out var addParent));
        Assert.Equal(JsonValueKind.False, addParent.ValueKind);
    }

    // ─── MapItem from Sidecar golden fixture ─────────────────────────────────

    [Fact]
    public void MapItem_MapsFieldsFromGoldenFixture()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = File.ReadAllText(Path.Combine(FixturesDir, "apify-instagram-item.json"));
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        // Platform
        Assert.Equal("instagram", snapshot.PlatformKey);

        // Author: ownerFullName takes priority over ownerUsername
        Assert.Equal("Humans of New York", snapshot.Author);

        // Title: caption (first line or truncated)
        Assert.NotNull(snapshot.Title);
        Assert.StartsWith("A few years ago", snapshot.Title);

        // Thumbnail
        Assert.Contains("cdninstagram.com", snapshot.ThumbnailUrl);

        // Canonical URL from "url" field
        Assert.Equal("https://www.instagram.com/p/DPwJzEVDbwC/", snapshot.CanonicalUrl);

        // Metrics: likes and comments; NO shares or saves
        Assert.Equal(256_532L, snapshot.Metrics["likes"]);
        Assert.Equal(3_951L, snapshot.Metrics["comments"]);
        Assert.False(snapshot.Metrics.ContainsKey("shares"), "Instagram has no shares metric");
        Assert.False(snapshot.Metrics.ContainsKey("saves"), "Instagram has no saves metric");

        // Sidecar: no video → views metric absent
        Assert.False(snapshot.Metrics.ContainsKey("views"), "Sidecar has no views metric");

        // No video duration for Sidecar
        Assert.Null(snapshot.DurationSeconds);
    }

    // ─── Content type (Sidecar → carousel) ───────────────────────────────────

    [Fact]
    public void MapItem_SetsContentTypeCarousel_ForSidecar()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = File.ReadAllText(Path.Combine(FixturesDir, "apify-instagram-item.json"));
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        // Sidecar → content-type/carousel MediaReference (same pattern as TikTok)
        Assert.Equal("carousel", snapshot.ContentType);
    }

    [Fact]
    public void MapItem_SetsContentTypeImage_ForImagePost()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "type": "Image",
              "caption": "An image post",
              "displayUrl": "https://cdn.example.com/img.jpg",
              "url": "https://www.instagram.com/p/abc/",
              "likesCount": 100,
              "commentsCount": 5,
              "ownerUsername": "testuser",
              "ownerFullName": null,
              "videoViewCount": null,
              "videoPlayCount": null,
              "videoDuration": null,
              "videoUrl": null,
              "hashtags": [],
              "timestamp": "2025-10-01T12:00:00.000Z"
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Contains(snapshot.MediaReferences,
            r => r.Kind.Contains("image", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot.MediaReferences,
            r => r.Kind.Contains("carousel", StringComparison.OrdinalIgnoreCase));
    }

    // ─── Video post: views, duration, video media reference ──────────────────

    [Fact]
    public void MapItem_MapsViewsAndDuration_ForVideoPost()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "type": "Video",
              "caption": "A reel",
              "displayUrl": "https://cdn.example.com/thumb.jpg",
              "videoUrl": "https://cdn.example.com/video.mp4",
              "url": "https://www.instagram.com/reel/xyz/",
              "likesCount": 5000,
              "commentsCount": 200,
              "videoViewCount": 80000,
              "videoPlayCount": null,
              "videoDuration": 30,
              "ownerUsername": "creator",
              "ownerFullName": null,
              "hashtags": [],
              "timestamp": "2025-11-01T09:00:00.000Z"
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal(80_000L, snapshot.Metrics["views"]);
        Assert.Equal(5_000L, snapshot.Metrics["likes"]);
        Assert.Equal(200L, snapshot.Metrics["comments"]);
        Assert.Equal(30, snapshot.DurationSeconds);

        // Video media reference present
        Assert.Contains(snapshot.MediaReferences,
            r => r.Kind == "video" && r.Uri == "https://cdn.example.com/video.mp4");

        // content-type/video reference
        Assert.Contains(snapshot.MediaReferences,
            r => r.Kind.Contains("video", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MapItem_FallsBackToVideoPlayCount_WhenViewCountNull()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "type": "Video",
              "caption": "A reel",
              "displayUrl": "https://cdn.example.com/thumb.jpg",
              "url": "https://www.instagram.com/reel/xyz/",
              "likesCount": 100,
              "commentsCount": 10,
              "videoViewCount": null,
              "videoPlayCount": 45000,
              "videoDuration": null,
              "videoUrl": null,
              "ownerUsername": "creator",
              "ownerFullName": null,
              "hashtags": [],
              "timestamp": "2025-11-01T09:00:00.000Z"
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal(45_000L, snapshot.Metrics["views"]);
    }

    // ─── Author fallback: ownerFullName null → ownerUsername ─────────────────

    [Fact]
    public void MapItem_FallsBackToUsername_WhenFullNameNull()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "type": "Image",
              "caption": "post",
              "displayUrl": "https://cdn.example.com/img.jpg",
              "url": "https://www.instagram.com/p/abc/",
              "likesCount": 10,
              "commentsCount": 1,
              "ownerFullName": null,
              "ownerUsername": "fallback_user",
              "videoViewCount": null,
              "videoPlayCount": null,
              "videoDuration": null,
              "videoUrl": null,
              "hashtags": [],
              "timestamp": "2025-10-01T12:00:00.000Z"
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("fallback_user", snapshot.Author);
    }

    // ─── Tags (hashtags as plain strings), ContentType, PublishedAtUtc ──────────

    [Fact]
    public void MapItem_MapsHashtagStringsToTags_WhenPresent()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "type": "Image",
              "caption": "post with tags #foo #bar",
              "displayUrl": "https://cdn.example.com/img.jpg",
              "url": "https://www.instagram.com/p/abc/",
              "likesCount": 10,
              "commentsCount": 1,
              "ownerUsername": "user",
              "ownerFullName": null,
              "videoViewCount": null,
              "videoPlayCount": null,
              "videoDuration": null,
              "videoUrl": null,
              "hashtags": ["foo", "bar"],
              "timestamp": "2025-10-01T12:00:00.000Z"
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Contains("foo", snapshot.Tags);
        Assert.Contains("bar", snapshot.Tags);
    }

    [Fact]
    public void MapItem_ReturnsEmptyTags_WhenHashtagsEmpty()
    {
        // The golden fixture (humansofny) has hashtags: []
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = File.ReadAllText(Path.Combine(FixturesDir, "apify-instagram-item.json"));
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Empty(snapshot.Tags);
    }

    [Fact]
    public void MapItem_SetsContentTypeCarousel_ForSidecarType()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = File.ReadAllText(Path.Combine(FixturesDir, "apify-instagram-item.json"));
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("carousel", snapshot.ContentType);
    }

    [Fact]
    public void MapItem_SetsContentTypeImage_ForImageType()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "type": "Image",
              "caption": "An image post",
              "displayUrl": "https://cdn.example.com/img.jpg",
              "url": "https://www.instagram.com/p/abc/",
              "likesCount": 100,
              "commentsCount": 5,
              "ownerUsername": "testuser",
              "ownerFullName": null,
              "videoViewCount": null,
              "videoPlayCount": null,
              "videoDuration": null,
              "videoUrl": null,
              "hashtags": [],
              "timestamp": "2025-10-01T12:00:00.000Z"
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("image", snapshot.ContentType);
    }

    [Fact]
    public void MapItem_SetsContentTypeVideo_ForVideoType()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "type": "Video",
              "caption": "A reel",
              "displayUrl": "https://cdn.example.com/thumb.jpg",
              "videoUrl": "https://cdn.example.com/video.mp4",
              "url": "https://www.instagram.com/reel/xyz/",
              "likesCount": 5000,
              "commentsCount": 200,
              "videoViewCount": 80000,
              "videoPlayCount": null,
              "videoDuration": 30,
              "ownerUsername": "creator",
              "ownerFullName": null,
              "hashtags": ["reel"],
              "timestamp": "2025-11-01T09:00:00.000Z"
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("video", snapshot.ContentType);
        Assert.Contains("reel", snapshot.Tags);
    }

    [Fact]
    public void MapItem_MapsPublishedAtUtc_FromTimestamp()
    {
        // The golden fixture has timestamp: "2025-10-13T14:14:46.000Z"
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = File.ReadAllText(Path.Combine(FixturesDir, "apify-instagram-item.json"));
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.NotNull(snapshot.PublishedAtUtc);
        Assert.Equal(
            new DateTimeOffset(2025, 10, 13, 14, 14, 46, TimeSpan.Zero),
            snapshot.PublishedAtUtc!.Value);
    }

    [Fact]
    public void MapItem_DoesNotUseMediaReference_ForContentType()
    {
        // ContentType is now a first-class field; NOT stored as a MediaReference kind.
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = File.ReadAllText(Path.Combine(FixturesDir, "apify-instagram-item.json"));
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.DoesNotContain(snapshot.MediaReferences,
            r => r.Kind.StartsWith("content-type/", StringComparison.OrdinalIgnoreCase));
    }

    // ─── Transcript always missing for IG ────────────────────────────────────

    [Fact]
    public void MapItem_AddsTranscriptMissingWarning()
    {
        var adapter = new InstagramApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "type": "Image",
              "caption": "just a photo",
              "displayUrl": "https://cdn.example.com/img.jpg",
              "url": "https://www.instagram.com/p/abc/",
              "likesCount": 5,
              "commentsCount": 0,
              "ownerUsername": "user",
              "ownerFullName": null,
              "videoViewCount": null,
              "videoPlayCount": null,
              "videoDuration": null,
              "videoUrl": null,
              "hashtags": [],
              "timestamp": "2025-10-01T12:00:00.000Z"
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Contains(snapshot.Warnings, w => w.Code == "transcript_missing");
    }
}
