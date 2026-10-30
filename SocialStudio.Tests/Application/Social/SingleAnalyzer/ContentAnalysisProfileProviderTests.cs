using SocialStudio.Application.Abstractions.ContentAnalysis;
using SocialStudio.Application.Features.ContentAnalysis;
using SocialStudio.Application.Features.Social.SingleAnalyzer.Profile;

namespace SocialStudio.Tests.Application.Social.SingleAnalyzer;

public sealed class ContentAnalysisProfileProviderTests
{
    [Fact]
    public void ForFlow_ReturnsSocialProfile_ForSingleAnalyzerKey()
    {
        var profiles = new IContentAnalysisProfile[] { new SocialContentAnalysisProfile() };
        var provider = new ContentAnalysisProfileProvider(profiles);

        var result = provider.ForFlow("single-analyzer");

        Assert.True(result.IsSuccess);
        Assert.IsType<SocialContentAnalysisProfile>(result.Value);
    }

    [Fact]
    public void ForFlow_ReturnsFailure_ForUnknownFlowKey()
    {
        var profiles = new IContentAnalysisProfile[] { new SocialContentAnalysisProfile() };
        var provider = new ContentAnalysisProfileProvider(profiles);

        var result = provider.ForFlow("product-analyzer");

        Assert.True(result.IsFailure);
    }
}
