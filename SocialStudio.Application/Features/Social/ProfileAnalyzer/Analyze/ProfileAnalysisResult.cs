namespace SocialStudio.Application.Features.Social.ProfileAnalyzer.Analyze;

/// <summary>
/// The JSON-deserialized AI output type for <c>IAiCompletionGateway.CompleteJsonAsync&lt;ProfileAnalysisResult&gt;</c>.
/// Field names match the JSON schema in design.md §AI output. Each field maps directly to an impute target.
/// </summary>
/// <remarks>
/// JSON schema (design.md):
/// {
///   "summary": "string",
///   "audienceProfile": "string",           // -> GeneralStrategy.TargetAudience
///   "contentPillars": ["string"],           // -> GeneralStrategy.ContentPillars
///   "topContentTypes": ["string"],
///   "postingDays": ["Mon","Wed","Fri"],     // -> GeneralStrategy.PostingFrequency
///   "detectedBrandColors": [{ "name": "string", "hex": "#RRGGBB" }], // -> BrandManual.ColorPalette
///   "detectedFonts": ["string"],            // -> BrandManual.Typography
///   "detectedBackgroundImages": ["url"],    // -> BrandManual.BackgroundTemplates
///   "voiceTone": "string",                  // -> BrandManual.VoiceAndTone
///   "recommendedKpis": ["string"],          // -> ContentPlan.ObjectiveKpis
///   "monthlyObjectiveSuggestion": "string", // -> ContentPlan.ObjectiveText
///   "contentStrategySuggestion": "string",  // -> ContentPlan.ContentStrategyText
///   "strengths": ["string"],
///   "weaknesses": ["string"],
///   "opportunities": ["string"],
///   "confidence": 0.0,                     // 0..1
///   "warnings": ["string"]
/// }
/// </remarks>
public sealed record ProfileAnalysisResult(
    string Summary,
    string AudienceProfile,
    IReadOnlyList<string> ContentPillars,
    IReadOnlyList<string> TopContentTypes,
    IReadOnlyList<string> PostingDays,
    IReadOnlyList<ProfileAnalysisColorResult> DetectedBrandColors,
    IReadOnlyList<string> DetectedFonts,
    IReadOnlyList<string> DetectedBackgroundImages,
    string VoiceTone,
    IReadOnlyList<string> RecommendedKpis,
    string MonthlyObjectiveSuggestion,
    string ContentStrategySuggestion,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<string> Opportunities,
    double Confidence,
    IReadOnlyList<string> Warnings);

/// <summary>A detected brand color entry in the AI output.</summary>
public sealed record ProfileAnalysisColorResult(string Name, string Hex);
