using System.Text.Json;
using Incoders.Hive.Infrastructure.Social.NicheDiscovery.Origins;

namespace Incoders.Hive.Tests.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// WU-5 (ND-04, ND-05, design §5.4): <see cref="YouTubeEvidenceMapper"/> is the ONLY type that sees
/// <see cref="JsonElement"/> for this origin. Tested entirely against a recorded fixture
/// (<c>Fixtures/youtube-niche-search.json</c>) — PENDING LIVE VERIFICATION, no live YouTube call is
/// made (see the class-level comment on the mapper and ADR-0012's precedent).
/// </summary>
public sealed class YouTubeEvidenceMapperTests
{
    private const string SourceKey = "youtube-data-api";

    private static readonly JsonElement Fixture = LoadFixture();

    [Fact]
    public void MapVideos_UnmappableItem_IsSkipped_NeverDefaulted()
    {
        var result = YouTubeEvidenceMapper.MapVideos(Fixture, SourceKey);

        Assert.Equal(3, result.TotalItems);
        Assert.Equal(2, result.Videos.Count);
        Assert.Equal(1, result.SkippedCount);
        Assert.DoesNotContain(result.Videos, v => v.ExternalRef == "vid-003-unmappable-no-title");
    }

    [Fact]
    public void MapVideos_FullyMappedItem_CarriesEveryField()
    {
        var result = YouTubeEvidenceMapper.MapVideos(Fixture, SourceKey);

        var video = result.Videos.Single(v => v.ExternalRef == "vid-001-fully-mapped");

        Assert.Equal("chan-001", video.ChannelExternalRef);
        Assert.Equal("How to grow a niche channel fast", video.Title);
        Assert.Equal("A full walkthrough of the growth playbook.", video.Description);
        Assert.Equal(125_000, video.Views);
        Assert.Equal(4_300, video.Likes);
        Assert.Equal(210, video.Comments);
        Assert.Equal(253, video.DurationSeconds); // PT4M13S = 4*60+13
        Assert.Equal("es", video.LanguageCode);
        Assert.Equal(SourceKey, video.SourceKey);
        Assert.NotNull(video.PublishedAtUtc);
    }

    [Fact]
    public void MapVideos_AbsentStatistics_MapsToNull_NeverZero()
    {
        var result = YouTubeEvidenceMapper.MapVideos(Fixture, SourceKey);

        var video = result.Videos.Single(v => v.ExternalRef == "vid-002-hidden-statistics");

        Assert.Null(video.Views);
        Assert.Null(video.Likes);
        Assert.Null(video.Comments);
    }

    [Fact]
    public void MapVideos_ZeroOfManyMapped_FromANonEmptyResponse_ReportsAllSkipped()
    {
        var allUnmappable = JsonSerializer.SerializeToElement(new
        {
            items = new object[]
            {
                new { id = "no-title-1", snippet = new { channelId = "chan-x" } },
                new { id = "no-title-2", snippet = new { channelId = "chan-y" } },
            },
        });

        var result = YouTubeEvidenceMapper.MapVideos(allUnmappable, SourceKey);

        Assert.Equal(2, result.TotalItems);
        Assert.Empty(result.Videos);
        Assert.Equal(2, result.SkippedCount);
    }

    [Fact]
    public void MapVideos_EmptyItemsArray_MapsToEmptyResult()
    {
        var empty = JsonSerializer.SerializeToElement(new { items = Array.Empty<object>() });

        var result = YouTubeEvidenceMapper.MapVideos(empty, SourceKey);

        Assert.Equal(0, result.TotalItems);
        Assert.Empty(result.Videos);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public void MapChannels_UnmappableItem_IsSkipped_MappableItemCarriesEveryField()
    {
        var response = JsonSerializer.SerializeToElement(new
        {
            items = new object[]
            {
                new
                {
                    id = "chan-001",
                    snippet = new
                    {
                        title = "Growth Playbook",
                        country = "AR",
                        defaultLanguage = "es",
                        publishedAt = "2020-01-01T00:00:00Z",
                    },
                    statistics = new
                    {
                        subscriberCount = "50000",
                        viewCount = "9000000",
                        videoCount = "120",
                    },
                },
                new { id = "chan-002-no-title", snippet = new { country = "AR" } },
            },
        });

        var result = YouTubeEvidenceMapper.MapChannels(response, SourceKey);

        Assert.Equal(2, result.TotalItems);
        Assert.Single(result.Channels);

        var channel = result.Channels.Single();
        Assert.Equal("chan-001", channel.ExternalRef);
        Assert.Equal("Growth Playbook", channel.Handle);
        Assert.Equal("AR", channel.Country);
        Assert.Equal(50_000, channel.SubscriberCount);
        Assert.Equal(9_000_000, channel.TotalViews);
        Assert.Equal(120, channel.VideoCount);
        Assert.Equal("es", channel.LanguageCode);
    }

    [Fact]
    public void MapChannels_HiddenSubscriberCount_MapsToNull_NeverZero()
    {
        var response = JsonSerializer.SerializeToElement(new
        {
            items = new object[]
            {
                new
                {
                    id = "chan-hidden",
                    snippet = new { title = "Hidden subs channel" },
                    statistics = new { hiddenSubscriberCount = true, viewCount = "1000" },
                },
            },
        });

        var result = YouTubeEvidenceMapper.MapChannels(response, SourceKey);

        var channel = Assert.Single(result.Channels);
        Assert.Null(channel.SubscriberCount);
        Assert.Equal(1_000, channel.TotalViews);
    }

    private static JsonElement LoadFixture()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Infrastructure", "Social", "Fixtures",
            "youtube-niche-search.json");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
