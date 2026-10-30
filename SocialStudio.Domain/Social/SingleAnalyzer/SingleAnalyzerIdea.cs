using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Social.SingleAnalyzer;

public sealed record SingleAnalyzerIdea(
    string Title,
    string Concept,
    string Rationale,
    SingleAnalyzerScore Score)
{
    public static Result<SingleAnalyzerIdea> Create(
        string title,
        string concept,
        string rationale,
        SingleAnalyzerScore score)
    {
        var idea = new SingleAnalyzerIdea(
            Normalize(title),
            Normalize(concept),
            Normalize(rationale),
            score);

        return idea.IsValid()
            ? Result.Success(idea)
            : Result.Failure<SingleAnalyzerIdea>(SingleAnalyzerErrors.AnalysisInvalid);
    }

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Title)
        && !string.IsNullOrWhiteSpace(Concept)
        && !string.IsNullOrWhiteSpace(Rationale)
        && Score is not null
        && Score.IsValid();

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();
}
