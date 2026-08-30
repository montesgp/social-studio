using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

namespace Incoders.Hive.Tests.Application.Abstractions.Social;

/// <summary>
/// WU-1 (D2, CD-04): metrics are plain nullable — <c>null</c> means the network does not report the
/// metric, <c>0</c> means it reported zero. No parallel (value, isAvailable) wrapper.
/// </summary>
public sealed class DiscoveredContentTests
{
    [Fact]
    public void MetricFields_DefaultToUnavailable_WhenNotSupplied()
    {
        var content = new DiscoveredContent(
            Title: "t",
            Summary: "s",
            NetworkSource: "instagram",
            ContentUrl: "https://instagram.com/p/1/",
            ThumbnailUrl: null,
            Views: null,
            Likes: null,
            Comments: null,
            Shares: null,
            Saves: null,
            PublishedAtUtc: null,
            SourceHandle: "@ref",
            SourceKey: "references");

        Assert.Null(content.Views);
        Assert.Null(content.Likes);
        Assert.Null(content.Comments);
        Assert.Null(content.Shares);
        Assert.Null(content.Saves);
        Assert.Null(content.PublishedAtUtc);
    }

    [Fact]
    public void MetricFields_DistinguishReportedZero_FromUnavailable()
    {
        var content = new DiscoveredContent(
            Title: "t",
            Summary: "s",
            NetworkSource: "tiktok",
            ContentUrl: "https://tiktok.com/@a/video/1",
            ThumbnailUrl: null,
            Views: 0,
            Likes: 0,
            Comments: 0,
            Shares: 40,
            Saves: null,
            PublishedAtUtc: DateTimeOffset.UtcNow,
            SourceHandle: "@a",
            SourceKey: "niche");

        // Reported zero is a real value, not the absence of one.
        Assert.Equal(0, content.Views);
        Assert.Equal(0, content.Likes);
        Assert.Equal(0, content.Comments);
        Assert.Equal(40, content.Shares);
        // Unreported stays null even alongside reported-zero siblings.
        Assert.Null(content.Saves);
    }

    [Fact]
    public void SourceKey_IdentifiesTheSourceThatProducedIt()
    {
        var content = new DiscoveredContent(
            "t", "s", "tiktok", "https://tiktok.com/@a/video/1", null,
            null, null, null, null, null, null, "@a", SourceKey: "niche");

        Assert.Equal("niche", content.SourceKey);
    }
}
