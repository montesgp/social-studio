using SocialStudio.Application.Abstractions.ContentAnalysis;
using SocialStudio.Application.Features.Social.ProfileAnalyzer.Analyze;

namespace SocialStudio.Application.Features.Social.ProfileAnalyzer.Profile;

/// <summary>
/// Content-analysis profile for the Profile Analyzer module.
/// FlowKey = "profile-analyzer". Registered alongside SocialContentAnalysisProfile so
/// IContentAnalysisProfileProvider.ForFlow("profile-analyzer") resolves this instance.
/// No reference to Social namespace in the neutral core types — this class is the bridge.
/// </summary>
/// <remarks>
/// Design.md §Decision 4: dedicated profile-analyzer flow with its own prompt + strict JSON output.
/// The profile analyzer does NOT reuse the single-analyzer flow or its prompt.
/// </remarks>
public sealed class ProfileAnalyzerProfile : IContentAnalysisProfile
{
    public string FlowKey => "profile-analyzer";

    /// <summary>
    /// Extraction capability key — not used directly by the profile analyzer
    /// (profile scraping goes through IProfileScrapeResolver, not ISocialContentExtractor).
    /// Kept for interface completeness.
    /// </summary>
    public string ExtractionCapabilityKey => "social-extraction";

    /// <summary>Extraction provider key for reference; actual routing is via IProfileScrapeResolver.</summary>
    public string ExtractionProviderKey => "apify";

    public string AiCapabilityKey => "ai-analysis";

    public string PromptVersionKey => ProfileAnalyzerPromptVersions.AnalysisV1;

    /// <summary>
    /// Platform detection is not required for the profile analyzer flow —
    /// it always targets Instagram. Returns "instagram" unconditionally.
    /// </summary>
    public string DetectPlatform(string sourceUrl) => "instagram";
}
