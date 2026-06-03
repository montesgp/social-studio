namespace Incoders.Hive.Domain.Social.ProfileAnalysis;

/// <summary>
/// Lifecycle status of a profile analysis run.
/// </summary>
/// <remarks>
/// Valid transitions:
///   Requested → ScrapingProfile → ProfileScraped | ProfileScrapedPartial
///             → Analyzing → Analyzed (terminal success)
///   Any non-terminal → Failed (terminal failure)
/// </remarks>
public enum ProfileAnalysisStatus
{
    Requested = 1,
    ScrapingProfile = 2,
    ProfileScraped = 3,
    ProfileScrapedPartial = 4,
    Analyzing = 5,
    Analyzed = 6,
    Failed = 7,
}
