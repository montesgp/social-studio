using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Social.SingleAnalyzer;

public sealed record SingleAnalyzerAnalysis(
    string Summary,
    IReadOnlyList<string> DetectedHooks,
    string LikelyAudience,
    string NarrativeStructure,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Weaknesses,
    IReadOnlyList<SingleAnalyzerIdea> DerivedIdeas,
    IReadOnlyList<string> ClientAdaptationRecommendations,
    double Confidence,
    IReadOnlyList<string> Warnings,
    PatternSet? Patterns = null)
{
    public static Result<SingleAnalyzerAnalysis> Create(
        string summary,
        IReadOnlyList<string> detectedHooks,
        string likelyAudience,
        string narrativeStructure,
        IReadOnlyList<string> strengths,
        IReadOnlyList<string> weaknesses,
        IReadOnlyList<SingleAnalyzerIdea> derivedIdeas,
        IReadOnlyList<string> clientAdaptationRecommendations,
        double confidence,
        IReadOnlyList<string> warnings,
        PatternSet? patterns = null)
    {
        var analysis = new SingleAnalyzerAnalysis(
            NormalizeRequired(summary),
            NormalizeList(detectedHooks),
            NormalizeRequired(likelyAudience),
            NormalizeRequired(narrativeStructure),
            NormalizeList(strengths),
            NormalizeList(weaknesses),
            derivedIdeas?.ToArray() ?? [],
            NormalizeList(clientAdaptationRecommendations),
            confidence,
            NormalizeList(warnings),
            patterns);

        var validation = analysis.Validate();
        return validation.IsSuccess
            ? Result.Success(analysis)
            : Result.Failure<SingleAnalyzerAnalysis>(validation.Error);
    }

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Summary)
            || DetectedHooks is null
            || DetectedHooks.Count == 0
            || string.IsNullOrWhiteSpace(LikelyAudience)
            || string.IsNullOrWhiteSpace(NarrativeStructure)
            || Strengths is null
            || Strengths.Count == 0
            || Weaknesses is null
            || Weaknesses.Count == 0
            || DerivedIdeas is null
            || DerivedIdeas.Count == 0
            || ClientAdaptationRecommendations is null
            || ClientAdaptationRecommendations.Count == 0
            || Confidence is < 0 or > 1
            || DerivedIdeas.Any(x => !x.IsValid()))
        {
            return Result.Failure(SingleAnalyzerErrors.AnalysisInvalid);
        }

        return Result.Success();
    }

    private static string NormalizeRequired(string? value) => (value ?? string.Empty).Trim();

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string>? values) =>
        (values ?? [])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .ToArray();
}
