namespace SocialStudio.Domain.Social.SingleAnalyzer;

public enum SingleContentAnalysisStatus
{
    Requested = 1,
    ResolvingContent = 2,
    ContentResolved = 3,
    ContentResolvedPartial = 4,
    Analyzing = 5,
    Analyzed = 6,
    Failed = 7,
}
