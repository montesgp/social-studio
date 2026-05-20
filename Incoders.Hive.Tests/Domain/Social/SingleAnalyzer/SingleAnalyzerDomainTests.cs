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
}
