namespace SocialStudio.Domain.Social.ProfileAnalysis;

/// <summary>
/// Value object carrying the structured AI output from the <c>profile-analyzer</c> flow.
/// Every field maps directly to an impute target in the client's brand manual, content plan,
/// or general strategy (see design.md §AI output schema and PA-11, PA-20).
/// No EF / JSON serialization types may be referenced here — serialization stays in Infrastructure.
/// </summary>
/// <param name="Summary">Executive summary of the account's marketing presence.</param>
/// <param name="AudienceProfile">Detected audience description. Maps to GeneralStrategy.TargetAudience.</param>
/// <param name="ContentPillars">Detected content pillars. Maps to GeneralStrategy.ContentPillars.</param>
/// <param name="TopContentTypes">Top-performing content types for this account.</param>
/// <param name="PostingDays">Recommended posting days derived from engagement patterns.
///     Maps to GeneralStrategy.PostingFrequency.</param>
/// <param name="DetectedBrandColors">Detected brand colors. Maps to BrandManual.ColorPalette.</param>
/// <param name="DetectedFonts">Detected font names. Maps to BrandManual.Typography.</param>
/// <param name="DetectedBackgroundImages">Detected background image styles. Maps to BrandManual.BackgroundTemplates.</param>
/// <param name="VoiceTone">Detected voice/tone description. Maps to BrandManual.VoiceAndTone.</param>
/// <param name="RecommendedKpis">Recommended KPIs for the client. Maps to ContentPlan.ObjectiveKpis.</param>
/// <param name="MonthlyObjectiveSuggestion">Suggested monthly objective. Maps to ContentPlan.ObjectiveText.</param>
/// <param name="ContentStrategySuggestion">Content strategy suggestion. Maps to ContentPlan.ContentStrategyText.</param>
/// <param name="Strengths">Identified account strengths.</param>
/// <param name="Weaknesses">Identified account weaknesses or gaps.</param>
/// <param name="Opportunities">Growth opportunities detected.</param>
/// <param name="Confidence">AI confidence score in the 0.0–1.0 range (PA-11).</param>
/// <param name="Warnings">Warnings emitted when confidence is low or post sample is sparse (PA-11).</param>
public sealed record ProfileAiAnalysis(
    string Summary,
    string AudienceProfile,
    IReadOnlyList<string> ContentPillars,
    IReadOnlyList<string> TopContentTypes,
    IReadOnlyList<string> PostingDays,
    IReadOnlyList<DetectedBrandColor> DetectedBrandColors,
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

/// <summary>A detected brand color with a human-readable name and hex value.</summary>
public sealed record DetectedBrandColor(string Name, string Hex);
