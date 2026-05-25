using Incoders.Hive.Application.Features.Social.SingleAnalyzer.Profile;

namespace Incoders.Hive.Tests.Application.Social.SingleAnalyzer;

public sealed class SocialContentAnalysisProfileTests
{
    private static readonly SocialContentAnalysisProfile Profile = new();

    [Fact]
    public void FlowKey_IsSingleAnalyzer()
    {
        Assert.Equal("single-analyzer", Profile.FlowKey);
    }

    [Fact]
    public void DetectPlatform_ReturnsTiktok_ForTikTokUrl()
    {
        var result = Profile.DetectPlatform("https://www.tiktok.com/@brand/video/123");
        Assert.Equal("tiktok", result);
    }

    [Fact]
    public void DetectPlatform_ReturnsInstagram_ForInstagramUrl()
    {
        var result = Profile.DetectPlatform("https://www.instagram.com/reel/abc");
        Assert.Equal("instagram", result);
    }

    [Fact]
    public void DetectPlatform_ReturnsUnknown_ForUnrecognizedUrl()
    {
        var result = Profile.DetectPlatform("https://example.com/posts/abc");
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void ExtractionCapabilityKey_IsExpectedValue()
    {
        Assert.Equal("social-extraction", Profile.ExtractionCapabilityKey);
    }

    [Fact]
    public void AiCapabilityKey_IsExpectedValue()
    {
        Assert.Equal("ai-analysis", Profile.AiCapabilityKey);
    }

    [Fact]
    public void PromptVersionKey_IsExpectedValue()
    {
        Assert.Equal("single-analyzer.analysis.v1", Profile.PromptVersionKey);
    }
}
