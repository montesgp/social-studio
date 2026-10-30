using SocialStudio.Application.Abstractions.ContentAnalysis;
using SocialStudio.Domain.Social.SingleAnalyzer;

namespace SocialStudio.Application.Features.Social.SingleAnalyzer.Profile;

/// <summary>
/// Content-analysis profile for the Social Single Analyzer module.
/// FlowKey = "single-analyzer". Delegates platform detection to <see cref="SingleAnalyzerSourceUrl"/>.
/// No reference to Social namespace in the neutral core types — this class is the bridge.
/// </summary>
public sealed class SocialContentAnalysisProfile : IContentAnalysisProfile
{
    public string FlowKey => "single-analyzer";
    public string ExtractionCapabilityKey => "social-extraction";
    public string ExtractionProviderKey => "apify";
    public string AiCapabilityKey => "ai-analysis";
    public string PromptVersionKey => "single-analyzer.analysis.v1";

    /// <inheritdoc />
    public string DetectPlatform(string sourceUrl)
    {
        var result = SingleAnalyzerSourceUrl.Create(sourceUrl);
        return result.IsSuccess ? result.Value.PlatformKey : "unknown";
    }
}
