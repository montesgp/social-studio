using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Domain.Social.SingleAnalyzer;

public sealed class SingleAnalyzerDomainTests
{
    [Fact]
    public void SourceUrl_DetectsKnownPlatformsAndAllowsUnknownConnectors()
    {
        var tiktok = SingleAnalyzerSourceUrl.Create(" HTTPS://WWW.TIKTOK.COM/@brand/video/123#comments ");
        var generic = SingleAnalyzerSourceUrl.Create("https://example.com/posts/abc");

        Assert.True(tiktok.IsSuccess);
        Assert.Equal("tiktok", tiktok.Value.PlatformKey);
        Assert.Equal("https://www.tiktok.com/@brand/video/123", tiktok.Value.NormalizedUrl);

        Assert.True(generic.IsSuccess);
        Assert.Equal("unknown", generic.Value.PlatformKey);
        Assert.Equal("https://example.com/posts/abc", generic.Value.NormalizedUrl);
    }

    [Fact]
    public void Score_RejectsValuesOutsideOneToFive()
    {
        var result = SingleAnalyzerScore.Create(
            novelty: 6,
            clientFit: 4,
            platformFit: 4,
            hookQuality: 4,
            feasibility: 4,
            expectedEngagement: 4);

        Assert.True(result.IsFailure);
        Assert.Equal("social.single_analyzer.score_invalid", result.Error.Code);
    }

    [Fact]
    public void ContentSnapshot_Create_PreservesTagsContentTypeAndPublishedAt()
    {
        var publishedAt = new DateTimeOffset(2026, 5, 23, 10, 0, 0, TimeSpan.Zero);

        var result = SingleAnalyzerContentSnapshot.Create(
            " TikTok ",
            " https://www.tiktok.com/@brand/video/123 ",
            " Hook ",
            " Caption ",
            "@brand",
            "https://cdn.example.com/thumb.jpg",
            30,
            new Dictionary<string, long> { ["views"] = 1000 },
            " Transcript ",
            [new SingleAnalyzerMediaReference("video", "https://cdn.example.com/video.mp4", "video/mp4")],
            " provider://apify/run/1 ",
            ["strategy", "creator"],
            " video ",
            publishedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal("tiktok", result.Value.PlatformKey);
        Assert.Equal("video", result.Value.ContentType);
        Assert.Equal(publishedAt, result.Value.PublishedAtUtc);
        Assert.Equal(["strategy", "creator"], result.Value.Tags);
        Assert.Equal("Transcript", result.Value.Transcript);
    }

    [Fact]
    public void ContentSnapshot_Create_RejectsInvalidMediaReference()
    {
        var result = SingleAnalyzerContentSnapshot.Create(
            "tiktok",
            "https://www.tiktok.com/@brand/video/123",
            null,
            null,
            null,
            null,
            null,
            new Dictionary<string, long>(),
            null,
            [new SingleAnalyzerMediaReference("", "https://cdn.example.com/video.mp4", null)],
            null);

        Assert.True(result.IsFailure);
        Assert.Equal("social.single_analyzer.snapshot_invalid", result.Error.Code);
    }
}
