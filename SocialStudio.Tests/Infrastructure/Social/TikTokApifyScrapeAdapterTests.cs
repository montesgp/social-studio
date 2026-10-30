using System.Net;
using System.Text.Json;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Infrastructure.Social.Providers.Apify;

namespace SocialStudio.Tests.Infrastructure.Social;

public sealed class TikTokApifyScrapeAdapterTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Social", "Fixtures");

    private static SocialContentRequest BuildRequest(string url = "https://www.tiktok.com/@brand/video/123") =>
        new(Guid.NewGuid(), url, url, "tiktok", "social-extraction");

    // ─── BuildActorInput shape ──────────────────────────────────────────────

    [Fact]
    public void BuildActorInput_ProducesPostUrlsShape()
    {
        var adapter = new TikTokApifyScrapeAdapter();
        var request = BuildRequest();

        var input = adapter.BuildActorInput(request);
        var json = JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("postURLs", out var postUrls));
        Assert.Equal(JsonValueKind.Array, postUrls.ValueKind);
        Assert.Equal(request.NormalizedUrl, postUrls[0].GetString());

        Assert.True(root.TryGetProperty("resultsPerPage", out var rpp));
        Assert.Equal(1, rpp.GetInt32());

        Assert.True(root.TryGetProperty("shouldDownloadVideos", out var sdv));
        Assert.Equal(JsonValueKind.False, sdv.ValueKind);

        Assert.True(root.TryGetProperty("shouldDownloadCovers", out var sdc));
        Assert.Equal(JsonValueKind.False, sdc.ValueKind);

        Assert.True(root.TryGetProperty("shouldDownloadSubtitles", out var sds));
        Assert.Equal(JsonValueKind.False, sds.ValueKind);
    }

    // ─── MapItem from fixture ───────────────────────────────────────────────

    [Fact]
    public void MapItem_MapsAllMetricsAndAuthorFromCapturedFixture()
    {
        var adapter = new TikTokApifyScrapeAdapter();
        var request = BuildRequest();
        var json = File.ReadAllText(Path.Combine(FixturesDir, "apify-tiktok-item.json"));
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("brand", snapshot.Author);
        Assert.Equal(120_000L, snapshot.Metrics["views"]);
        Assert.Equal(8_500L, snapshot.Metrics["likes"]);
        Assert.Equal(342L, snapshot.Metrics["comments"]);
        Assert.Equal(1_200L, snapshot.Metrics["shares"]);
        Assert.Equal(950L, snapshot.Metrics["saves"]);
        Assert.Contains("sign.tiktokcdn", snapshot.ThumbnailUrl);
        Assert.Equal(38, snapshot.DurationSeconds);
    }

    [Fact]
    public void MapItem_SetsContentTypeSlideshow_WhenIsSlideshow()
    {
        var adapter = new TikTokApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "text": "slideshow post",
              "authorMeta": { "nickName": "author" },
              "videoMeta": { "duration": 0 },
              "playCount": 1, "diggCount": 1, "commentCount": 0, "shareCount": 0, "collectCount": 0,
              "webVideoUrl": "https://www.tiktok.com/@author/video/1",
              "isSlideshow": true
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("slideshow", snapshot.ContentType);
        // Verify via MediaReferences or a dedicated field — adapter must surface contentType in snapshot.
        // We check the PlatformKey is "tiktok" and the warning list doesn't suppress the type.
        // The contentType is conveyed via RawPayloadReference containing "slideshow" or via a dedicated
        // approach. Per design: contentType stored as custom metric or media reference kind.
        Assert.Equal("tiktok", snapshot.PlatformKey);
        // Per the design: isSlideshow=true => a MediaReference with kind "slideshow" or
        // the ContentType is embedded. Check that a reference with kind containing "slideshow" exists.
        Assert.Equal("tiktok", snapshot.PlatformKey);
    }

    [Fact]
    public void MapItem_SetsContentTypeVideo_WhenNotSlideshow()
    {
        var adapter = new TikTokApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "text": "video post",
              "authorMeta": { "nickName": "author" },
              "videoMeta": { "duration": 30, "coverUrl": "https://cdn.example.com/cover.jpg" },
              "playCount": 100, "diggCount": 10, "commentCount": 5, "shareCount": 2, "collectCount": 1,
              "webVideoUrl": "https://www.tiktok.com/@author/video/2",
              "isSlideshow": false
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("tiktok", snapshot.PlatformKey);
        // Not a slideshow: should NOT have a slideshow reference
        Assert.Equal("video", snapshot.ContentType);
    }

    [Fact]
    public void MapItem_AddsTranscriptMissingWarning()
    {
        var adapter = new TikTokApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "text": "no transcript",
              "authorMeta": { "nickName": "author" },
              "videoMeta": { "duration": 15 },
              "playCount": 50, "diggCount": 5, "commentCount": 1, "shareCount": 0, "collectCount": 0,
              "webVideoUrl": "https://www.tiktok.com/@author/video/3",
              "isSlideshow": false
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Contains(snapshot.Warnings, w => w.Code == "transcript_missing");
    }

    // ─── Tags (hashtags[].name), ContentType, PublishedAtUtc ───────────────────

    [Fact]
    public void MapItem_MapsHashtagsToTags_FromCapturedFixture()
    {
        // The fixture has hashtags: [{ "name": "contentstrategy" }, { "name": "socialmedia" }]
        var adapter = new TikTokApifyScrapeAdapter();
        var request = BuildRequest();
        var json = File.ReadAllText(Path.Combine(FixturesDir, "apify-tiktok-item.json"));
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Contains("contentstrategy", snapshot.Tags);
        Assert.Contains("socialmedia", snapshot.Tags);
    }

    [Fact]
    public void MapItem_SetsContentTypeVideo_FromIsSlideshow_False()
    {
        var adapter = new TikTokApifyScrapeAdapter();
        var request = BuildRequest();
        var json = File.ReadAllText(Path.Combine(FixturesDir, "apify-tiktok-item.json"));
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("video", snapshot.ContentType);
    }

    [Fact]
    public void MapItem_SetsContentTypeSlideshow_WhenIsSlideshowTrue()
    {
        var adapter = new TikTokApifyScrapeAdapter();
        var request = BuildRequest();
        var json = """
            {
              "text": "slideshow content",
              "authorMeta": { "nickName": "author" },
              "videoMeta": { "duration": 0 },
              "playCount": 1, "diggCount": 1, "commentCount": 0, "shareCount": 0, "collectCount": 0,
              "webVideoUrl": "https://www.tiktok.com/@author/video/1",
              "isSlideshow": true,
              "hashtags": [],
              "createTimeISO": "2024-03-01T09:00:00.000Z"
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("slideshow", snapshot.ContentType);
    }

    [Fact]
    public void MapItem_MapsPublishedAtUtc_FromCreateTimeISO()
    {
        // The fixture has createTimeISO: "2024-05-01T12:00:00.000Z"
        var adapter = new TikTokApifyScrapeAdapter();
        var request = BuildRequest();
        var json = File.ReadAllText(Path.Combine(FixturesDir, "apify-tiktok-item.json"));
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.NotNull(snapshot.PublishedAtUtc);
        Assert.Equal(
            new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero),
            snapshot.PublishedAtUtc!.Value);
    }

    [Fact]
    public void MapItem_DoesNotUseMediaReference_ForContentType()
    {
        // ContentType is now a first-class field; NOT stored as a MediaReference kind.
        var adapter = new TikTokApifyScrapeAdapter();
        var request = BuildRequest();
        var json = File.ReadAllText(Path.Combine(FixturesDir, "apify-tiktok-item.json"));
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        // No "content-type/*" media reference hack should remain
        Assert.DoesNotContain(snapshot.MediaReferences,
            r => r.Kind.StartsWith("content-type/", StringComparison.OrdinalIgnoreCase));
    }
}
