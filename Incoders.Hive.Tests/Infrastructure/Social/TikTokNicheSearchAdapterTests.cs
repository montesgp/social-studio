using System.Text.Json;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;

namespace Incoders.Hive.Tests.Infrastructure.Social;

/// <summary>
/// <see cref="TikTokNicheSearchAdapter"/> tested against the recorded (documentation-derived,
/// owner-waived-spike) fixture only — no live Apify call. design §7/§7.1.
/// </summary>
public sealed class TikTokNicheSearchAdapterTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Social", "Fixtures");

    private static JsonElement LoadItems()
    {
        var json = File.ReadAllText(Path.Combine(FixturesDir, "tiktok-niche-search.json"));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void BuildSearchQueriesInput_ProducesSearchQueriesShape()
    {
        var adapter = new TikTokNicheSearchAdapter();

        var input = adapter.BuildSearchQueriesInput("content strategy", resultsPerPage: 20);
        var json = JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("searchQueries", out var queries));
        Assert.Equal(JsonValueKind.Array, queries.ValueKind);
        Assert.Equal("content strategy", queries[0].GetString());

        Assert.True(root.TryGetProperty("resultsPerPage", out var rpp));
        Assert.Equal(20, rpp.GetInt32());

        Assert.True(root.TryGetProperty("shouldDownloadVideos", out var sdv));
        Assert.Equal(JsonValueKind.False, sdv.ValueKind);
    }

    [Fact]
    public void MapItem_MapsAllMetricsAuthorAndUrl_FromFirstFixtureItem()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var items = LoadItems();
        var first = items[0];

        var mapped = adapter.MapItem(first);

        Assert.NotNull(mapped);
        Assert.Equal("nichecreator1", mapped!.SourceHandle);
        Assert.Equal("tiktok", mapped.NetworkSource);
        Assert.Equal("niche", mapped.SourceKey);
        Assert.Equal("https://www.tiktok.com/@nichecreator1/video/7391000000000000001", mapped.ContentUrl);
        Assert.Equal(84_000L, mapped.Views);
        Assert.Equal(6_100L, mapped.Likes);
        Assert.Equal(210L, mapped.Comments);
        Assert.Equal(540L, mapped.Shares);
        Assert.Equal(390L, mapped.Saves);
        Assert.NotNull(mapped.PublishedAtUtc);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 20, 9, 15, 0, TimeSpan.Zero),
            mapped.PublishedAtUtc!.Value);
    }

    [Fact]
    public void MapItem_DegradesToNull_WhenShareAndSaveCountsAreAbsent()
    {
        // Second fixture item has no shareCount/collectCount — per design §7 degradation table,
        // these stay null (excluded from the scorer denominator), never zero-filled.
        var adapter = new TikTokNicheSearchAdapter();
        var items = LoadItems();
        var second = items[1];

        var mapped = adapter.MapItem(second);

        Assert.NotNull(mapped);
        Assert.Null(mapped!.Shares);
        Assert.Null(mapped.Saves);
        Assert.Equal(51_000L, mapped.Views);
    }

    [Fact]
    public void MapItem_DegradesPublishedAtToNull_WhenCreateTimeIsoAbsent()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var json = """{ "text": "no timestamp", "authorMeta": { "nickName": "author" }, "playCount": 10, "webVideoUrl": "https://www.tiktok.com/@author/video/1" }""";
        var item = JsonDocument.Parse(json).RootElement;

        var mapped = adapter.MapItem(item);

        Assert.NotNull(mapped);
        Assert.Null(mapped!.PublishedAtUtc);
    }

    [Fact]
    public void MapItem_ReturnsNull_WhenWebVideoUrlIsMissing()
    {
        // The third fixture item has no webVideoUrl — it is skipped, never defaulted to an invented URL.
        var adapter = new TikTokNicheSearchAdapter();
        var items = LoadItems();
        var third = items[2];

        var mapped = adapter.MapItem(third);

        Assert.Null(mapped);
    }

    [Fact]
    public void MapItems_ZeroOfManyMapped_ReturnsEmptyMappedList()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var json = """
            [
              { "text": "no url one" },
              { "text": "no url two" }
            ]
            """;
        var items = JsonDocument.Parse(json).RootElement;

        var (mapped, totalCount, skippedCount) = adapter.MapItems(items);

        Assert.Empty(mapped);
        Assert.Equal(2, totalCount);
        Assert.Equal(2, skippedCount);
    }

    [Fact]
    public void MapItems_PartialMap_ReturnsWhatMapped_AndReportsSkipped()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var items = LoadItems();

        var (mapped, totalCount, skippedCount) = adapter.MapItems(items);

        Assert.Equal(2, mapped.Count);
        Assert.Equal(3, totalCount);
        Assert.Equal(1, skippedCount);
    }

    [Fact]
    public void MapItem_NeverThrows_OnMalformedItemShape()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var json = """{ "authorMeta": "not-an-object", "videoMeta": 42, "hashtags": "not-an-array" }""";
        var item = JsonDocument.Parse(json).RootElement;

        var mapped = adapter.MapItem(item);

        Assert.Null(mapped);
    }
}
