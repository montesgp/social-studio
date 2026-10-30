namespace SocialStudio.Application.Features.Social.ProfileAnalyzer.Analyze;

/// <summary>
/// Prompt version keys for the profile analyzer.
/// Each key is persisted with every analysis record and usage event
/// so outputs can be correlated with the exact prompt that generated them.
/// </summary>
public static class ProfileAnalyzerPromptVersions
{
    /// <summary>
    /// Initial profile analysis prompt. Output schema covers:
    /// summary, audienceProfile, contentPillars, topContentTypes, postingDays,
    /// detectedBrandColors, detectedFonts, detectedBackgroundImages, voiceTone,
    /// recommendedKpis, monthlyObjectiveSuggestion, contentStrategySuggestion,
    /// strengths, weaknesses, opportunities, confidence, warnings.
    /// Each field maps to a specific impute target (PA-11, PA-20).
    /// </summary>
    public const string AnalysisV1 = "profile-analyzer.analysis.v1";
}
