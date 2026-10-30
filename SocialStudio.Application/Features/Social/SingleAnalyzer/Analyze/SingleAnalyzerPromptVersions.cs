namespace SocialStudio.Application.Features.Social.SingleAnalyzer.Analyze;

/// <summary>
/// Prompt version keys for the single-content analyzer.
/// Each key is persisted with every analysis record and usage event
/// so we can correlate outputs with the exact prompt that generated them.
/// </summary>
public static class SingleAnalyzerPromptVersions
{
    /// <summary>Original analysis prompt — pattern extraction NOT included.</summary>
    public const string V1 = "single-analyzer.analysis.v1";

    /// <summary>
    /// Pattern extraction added (PatternSet).
    /// Not yet wired into the live analyzer — will be enabled via config in Phase 3.
    /// </summary>
    public const string V2 = "single-analyzer.analysis.v2";
}
