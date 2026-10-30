using System.Text.Json;
using SocialStudio.Infrastructure.Social.Providers.Apify;

namespace SocialStudio.Tests.Infrastructure.Social;

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

    private static JsonElement LoadFlattenedItems()
    {
        var json = File.ReadAllText(Path.Combine(FixturesDir, "tiktok-niche-search-flattened.json"));
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

        var (mapped, totalCount, skippedCount, _) = adapter.MapItems(items);

        Assert.Empty(mapped);
        Assert.Equal(2, totalCount);
        Assert.Equal(2, skippedCount);
    }

    [Fact]
    public void MapItems_PartialMap_ReturnsWhatMapped_AndReportsSkipped()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var items = LoadItems();

        var (mapped, totalCount, skippedCount, _) = adapter.MapItems(items);

        Assert.Equal(2, mapped.Count);
        Assert.Equal(3, totalCount);
        Assert.Equal(1, skippedCount);
    }

    [Fact]
    public void MapItem_MapsFlattenedDotNotationItem_FromRealActorPayload()
    {
        // The operator's configured TikTok Discover actor flattens nested objects into dot-notation
        // property names. SourceHandle is the join key for the account baseline behind relative
        // performance, so reading only the nested shape silently drops the strongest ranking signal.
        var adapter = new TikTokNicheSearchAdapter();
        var first = LoadFlattenedItems()[0];

        var mapped = adapter.MapItem(first);

        Assert.NotNull(mapped);
        Assert.Equal("jhonahmed9", mapped!.SourceHandle);
        Assert.Equal("tiktok", mapped.NetworkSource);
        Assert.Equal("niche", mapped.SourceKey);
        Assert.Equal("https://www.tiktok.com/@jhonahmed9/video/7640167678669458690", mapped.ContentUrl);
        Assert.Equal(214_300L, mapped.Views);
        Assert.Equal(3_389L, mapped.Likes);
        Assert.Equal(38L, mapped.Comments);
        Assert.Equal(243L, mapped.Shares);
        Assert.Null(mapped.Saves);
        Assert.Equal(
            new DateTimeOffset(2026, 5, 15, 17, 12, 48, TimeSpan.Zero),
            mapped.PublishedAtUtc!.Value);
        Assert.StartsWith("Don't Skip This after updates", mapped.Title, StringComparison.Ordinal);
        Assert.Contains("#tipsandtricks", mapped.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void MapItem_LeavesThumbnailNull_WhenActorReportsNoVideoCover()
    {
        // The flattened payload carries authorMeta.avatar but NO video cover. The avatar is the
        // author's profile picture, not the video still — falling back to it would render a wall of
        // faces dressed as thumbnails. An absent thumbnail is honest; a misleading one is not.
        var adapter = new TikTokNicheSearchAdapter();
        var first = LoadFlattenedItems()[0];

        var mapped = adapter.MapItem(first);

        Assert.NotNull(mapped);
        Assert.Null(mapped!.ThumbnailUrl);
    }

    [Fact]
    public void MapItem_NeverFallsBackToAuthorAvatar_ForThumbnail_InNestedShape()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var json = """{ "text": "no cover", "authorMeta": { "nickName": "author", "avatar": "https://cdn/avatar.jpg" }, "webVideoUrl": "https://www.tiktok.com/@author/video/1" }""";
        var item = JsonDocument.Parse(json).RootElement;

        var mapped = adapter.MapItem(item);

        Assert.NotNull(mapped);
        Assert.Null(mapped!.ThumbnailUrl);
    }

    [Fact]
    public void MapItem_ReadsFlattenedVideoCover_WhenTheActorReportsOne()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var json = """{ "text": "flattened cover", "authorMeta.name": "author", "videoMeta.coverUrl": "https://cdn/cover.jpg", "webVideoUrl": "https://www.tiktok.com/@author/video/1" }""";
        var item = JsonDocument.Parse(json).RootElement;

        var mapped = adapter.MapItem(item);

        Assert.NotNull(mapped);
        Assert.Equal("https://cdn/cover.jpg", mapped!.ThumbnailUrl);
    }

    [Fact]
    public void MapItem_ReadsCoversDefault_InBothShapes()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var nested = JsonDocument.Parse(
            """{ "authorMeta": { "name": "author" }, "covers": { "default": "https://cdn/nested-cover.jpg" }, "webVideoUrl": "https://www.tiktok.com/@author/video/1" }""").RootElement;
        var flattened = JsonDocument.Parse(
            """{ "authorMeta.name": "author", "covers.default": "https://cdn/flat-cover.jpg", "webVideoUrl": "https://www.tiktok.com/@author/video/2" }""").RootElement;

        Assert.Equal("https://cdn/nested-cover.jpg", new TikTokNicheSearchAdapter().MapItem(nested)!.ThumbnailUrl);
        Assert.Equal("https://cdn/flat-cover.jpg", adapter.MapItem(flattened)!.ThumbnailUrl);
    }

    [Fact]
    public void MapItems_MapsEveryItem_InTheFlattenedFixture()
    {
        var adapter = new TikTokNicheSearchAdapter();

        var (mapped, totalCount, skippedCount, _) = adapter.MapItems(LoadFlattenedItems());

        Assert.Single(mapped);
        Assert.Equal(1, totalCount);
        Assert.Equal(0, skippedCount);
    }

    [Fact]
    public void MapItem_ReturnsNull_WhenFlattenedItemHasNoWebVideoUrl()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var json = """{ "authorMeta.name": "author", "playCount": 10 }""";
        var item = JsonDocument.Parse(json).RootElement;

        Assert.Null(adapter.MapItem(item));
    }

    [Fact]
    public void MapItems_ClassifiesErrorItemsAsProviderErrors_NotAsUnmappableItems()
    {
        // The actor reports a non-existent hashtag as an ITEM carrying error/errorCode. That is the
        // provider answering, not a response shape the mapping fails to recognise.
        var adapter = new TikTokNicheSearchAdapter();
        var items = JsonDocument.Parse("""
            [
              {
                "error": "This profile/hashtag does not exist.",
                "errorCode": "NOT_FOUND",
                "url": "https://www.tiktok.com/discover/medicalhealth",
                "input": "medicalhealth"
              }
            ]
            """).RootElement;

        var (mapped, totalCount, skippedCount, providerErrors) = adapter.MapItems(items);

        Assert.Empty(mapped);
        Assert.Equal(1, totalCount);
        Assert.Equal(0, skippedCount);

        var error = Assert.Single(providerErrors);
        Assert.Equal("NOT_FOUND", error.ErrorCode);
        Assert.Equal("medicalhealth", error.Input);
        Assert.Equal("This profile/hashtag does not exist.", error.Message);
    }

    [Fact]
    public void MapItems_MixedErrorAndVideoItems_StillMapTheVideo()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var items = JsonDocument.Parse("""
            [
              { "error": "This profile/hashtag does not exist.", "errorCode": "NOT_FOUND", "input": "medicalhealth" },
              { "text": "real one", "authorMeta": { "nickName": "author" }, "webVideoUrl": "https://www.tiktok.com/@author/video/1" }
            ]
            """).RootElement;

        var (mapped, totalCount, skippedCount, providerErrors) = adapter.MapItems(items);

        Assert.Single(mapped);
        Assert.Equal(2, totalCount);
        Assert.Equal(0, skippedCount);
        Assert.Single(providerErrors);
    }

    [Fact]
    public void MapItems_UnmappableItemWithoutAnErrorField_IsStillCountedAsSkipped()
    {
        var adapter = new TikTokNicheSearchAdapter();
        var items = JsonDocument.Parse("""[ { "text": "no url" } ]""").RootElement;

        var (mapped, totalCount, skippedCount, providerErrors) = adapter.MapItems(items);

        Assert.Empty(mapped);
        Assert.Equal(1, totalCount);
        Assert.Equal(1, skippedCount);
        Assert.Empty(providerErrors);
    }

    [Fact]
    public void BuildHashtagsInput_SendsEveryDerivedCandidate_InOneCall()
    {
        var adapter = new TikTokNicheSearchAdapter();

        var input = adapter.BuildHashtagsInput(["medical", "health", "medicalhealth"], resultsPerPage: 20);
        var json = JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(
            ["medical", "health", "medicalhealth"],
            doc.RootElement.GetProperty("hashtags").EnumerateArray().Select(h => h.GetString()).ToArray());
        Assert.Equal(20, doc.RootElement.GetProperty("resultsPerPage").GetInt32());
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
