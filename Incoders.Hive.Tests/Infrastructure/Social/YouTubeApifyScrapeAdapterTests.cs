using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;

namespace Incoders.Hive.Tests.Infrastructure.Social;

/// <summary>
/// Unit tests for YouTubeApifyScrapeAdapter (generic actor streamers/youtube-scraper).
/// Field mappings verified against the golden fixture at apify-youtube-item.json (live response, obs 2026-06-02).
/// The same actor covers both Shorts and regular videos via a direct video URL.
/// </summary>
public sealed class YouTubeApifyScrapeAdapterTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Social", "Fixtures");

    private static SocialContentRequest BuildRequest(string url = "https://www.youtube.com/shorts/kJrvUw__f5E") =>
        new(Guid.NewGuid(), url, url, "youtube", "social-extraction");

    // ─── PlatformKey ─────────────────────────────────────────────────────────

    [Fact]
    public void PlatformKey_IsYouTube()
    {
        var adapter = new YouTubeApifyScrapeAdapter();
        Assert.Equal("youtube", adapter.PlatformKey);
    }

    // ─── BuildActorInput shape ────────────────────────────────────────────────

    [Fact]
    public void BuildActorInput_ProducesStartUrlsShape()
    {
        var adapter = new YouTubeApifyScrapeAdapter();
        var request = BuildRequest();

        var input = adapter.BuildActorInput(request);
        var json = JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("startUrls", out var startUrls));
        Assert.Equal(JsonValueKind.Array, startUrls.ValueKind);
        Assert.Equal(request.NormalizedUrl, startUrls[0].GetProperty("url").GetString());

        Assert.True(root.TryGetProperty("maxResults", out var maxResults));
        Assert.Equal(1, maxResults.GetInt32());

        Assert.True(root.TryGetProperty("downloadSubtitles", out var subtitles));
        Assert.Equal(JsonValueKind.False, subtitles.ValueKind);
    }

    // ─── MapItem from golden fixture (Shorts) ─────────────────────────────────

    [Fact]
    public void MapItem_MapsFieldsFromGoldenFixture()
    {
        var adapter = new YouTubeApifyScrapeAdapter();
        var request = BuildRequest();
        var item = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturesDir, "apify-youtube-item.json"))).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("youtube", snapshot.PlatformKey);
        Assert.StartsWith("Idea Millonaria de Apps", snapshot.Title);
        Assert.StartsWith("Copiar y pegar", snapshot.Description);
        Assert.Equal("Fernando Herrera", snapshot.Author);
        Assert.Contains("ytimg.com", snapshot.ThumbnailUrl);
        Assert.Equal("https://www.youtube.com/shorts/kJrvUw__f5E", snapshot.CanonicalUrl);

        Assert.Equal(36_969L, snapshot.Metrics["views"]);
        Assert.Equal(905L, snapshot.Metrics["likes"]);
        Assert.Equal(30L, snapshot.Metrics["comments"]);
        Assert.False(snapshot.Metrics.ContainsKey("shares"), "YouTube has no shares metric");
        Assert.False(snapshot.Metrics.ContainsKey("saves"), "YouTube has no saves metric");
    }

    // ─── ContentType passthrough (shorts) ─────────────────────────────────────

    [Fact]
    public void MapItem_SetsContentTypeShorts_ForShortsFixture()
    {
        var adapter = new YouTubeApifyScrapeAdapter();
        var request = BuildRequest();
        var item = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturesDir, "apify-youtube-item.json"))).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("shorts", snapshot.ContentType);
    }

    [Fact]
    public void MapItem_SetsContentTypeVideo_ForRegularVideo()
    {
        var adapter = new YouTubeApifyScrapeAdapter();
        var request = BuildRequest("https://www.youtube.com/watch?v=abc");
        var json = """
            {
              "title": "A regular video",
              "type": "video",
              "url": "https://www.youtube.com/watch?v=abc",
              "thumbnailUrl": "https://i.ytimg.com/vi/abc/hq.jpg",
              "channelName": "Some Channel",
              "viewCount": 1000,
              "likes": 50,
              "commentsCount": 8,
              "duration": "00:10:05",
              "date": "2025-12-01T08:00:00.000Z",
              "text": "A description",
              "descriptionLinks": [],
              "hashtags": []
            }
            """;
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, request);

        Assert.Equal("video", snapshot.ContentType);
        // "00:10:05" → 605 seconds
        Assert.Equal(605, snapshot.DurationSeconds);
    }

    // ─── Duration parsing ─────────────────────────────────────────────────────

    [Fact]
    public void MapItem_ParsesDuration_FromHhMmSs()
    {
        var adapter = new YouTubeApifyScrapeAdapter();
        var item = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturesDir, "apify-youtube-item.json"))).RootElement;

        var snapshot = adapter.MapItem(item, BuildRequest());

        // "00:01:26" → 86 seconds
        Assert.Equal(86, snapshot.DurationSeconds);
    }

    [Fact]
    public void MapItem_ParsesDuration_FromMmSs()
    {
        var adapter = new YouTubeApifyScrapeAdapter();
        var json = """{ "type": "video", "url": "https://youtu.be/x", "duration": "02:30" }""";
        var item = JsonDocument.Parse(json).RootElement;

        var snapshot = adapter.MapItem(item, BuildRequest());

        Assert.Equal(150, snapshot.DurationSeconds);
    }

    // ─── Tags from descriptionLinks hashtag links ─────────────────────────────

    [Fact]
    public void MapItem_ExtractsHashtags_FromDescriptionLinks()
    {
        var adapter = new YouTubeApifyScrapeAdapter();
        var item = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturesDir, "apify-youtube-item.json"))).RootElement;

        var snapshot = adapter.MapItem(item, BuildRequest());

        Assert.Contains("AppDevelopment", snapshot.Tags);
        Assert.Contains("shorts", snapshot.Tags);
        Assert.DoesNotContain(snapshot.Tags, t => t.StartsWith('#'));
    }

    // ─── PublishedAtUtc from date ─────────────────────────────────────────────

    [Fact]
    public void MapItem_MapsPublishedAtUtc_FromDate()
    {
        var adapter = new YouTubeApifyScrapeAdapter();
        var item = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturesDir, "apify-youtube-item.json"))).RootElement;

        var snapshot = adapter.MapItem(item, BuildRequest());

        Assert.NotNull(snapshot.PublishedAtUtc);
        Assert.Equal(
            new DateTimeOffset(2026, 5, 26, 10, 3, 44, TimeSpan.Zero),
            snapshot.PublishedAtUtc!.Value);
    }

    // ─── Transcript always missing ────────────────────────────────────────────

    [Fact]
    public void MapItem_AddsTranscriptMissingWarning()
    {
        var adapter = new YouTubeApifyScrapeAdapter();
        var item = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturesDir, "apify-youtube-item.json"))).RootElement;

        var snapshot = adapter.MapItem(item, BuildRequest());

        Assert.Contains(snapshot.Warnings, w => w.Code == "transcript_missing");
    }
}
