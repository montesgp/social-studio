using SocialStudio.Domain.Social.SingleAnalyzer;

namespace SocialStudio.Tests.Domain.Social.SingleAnalyzer;

/// <summary>
/// Tests for the additive PatternSet? Patterns field on SingleAnalyzerAnalysis.
/// Existing analyses default Patterns to null (backward compatibility).
/// </summary>
public sealed class SingleAnalyzerAnalysisPatternsTests
{
    private static readonly IReadOnlyList<string> ValidHooks = new[] { "Hook 1" };
    private static readonly IReadOnlyList<string> ValidStrengths = new[] { "Strength 1" };
    private static readonly IReadOnlyList<string> ValidWeaknesses = new[] { "Weakness 1" };
    private static readonly IReadOnlyList<string> ValidRecommendations = new[] { "Recommendation 1" };

    private static readonly IReadOnlyList<SingleAnalyzerIdea> ValidIdeas = new[]
    {
        SingleAnalyzerIdea.Create(
            "Idea title",
            "Idea concept",
            "Idea rationale",
            SingleAnalyzerScore.Create(4, 4, 4, 4, 4, 4).Value).Value
    };

    [Fact]
    public void Create_WithoutPatterns_PatternFieldIsNull()
    {
        var result = SingleAnalyzerAnalysis.Create(
            "Summary",
            ValidHooks,
            "Likely audience",
            "Narrative structure",
            ValidStrengths,
            ValidWeaknesses,
            ValidIdeas,
            ValidRecommendations,
            0.8,
            Array.Empty<string>());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Patterns);
    }

    [Fact]
    public void Create_WithPatterns_PatternFieldIsPreserved()
    {
        var patterns = PatternSet.Create(
            new PatternItem[] { PatternItem.Create("Hook pattern", "Uses open loops", 0.9).Value },
            Array.Empty<PatternItem>(),
            Array.Empty<PatternItem>()).Value;

        var result = SingleAnalyzerAnalysis.Create(
            "Summary",
            ValidHooks,
            "Likely audience",
            "Narrative structure",
            ValidStrengths,
            ValidWeaknesses,
            ValidIdeas,
            ValidRecommendations,
            0.8,
            Array.Empty<string>(),
            patterns);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Patterns);
        Assert.Single(result.Value.Patterns!.CommunicationPatterns);
        Assert.Equal("Hook pattern", result.Value.Patterns!.CommunicationPatterns[0].Name);
    }

    [Fact]
    public void Create_ExistingOverloadWithoutPatterns_StillSucceeds_NoBreakingChange()
    {
        // Verifying the optional parameter does not break existing callers
        // that pass only the 10-argument form.
        var result = SingleAnalyzerAnalysis.Create(
            "Summary",
            ValidHooks,
            "Audience",
            "Narrative",
            ValidStrengths,
            ValidWeaknesses,
            ValidIdeas,
            ValidRecommendations,
            0.75,
            Array.Empty<string>());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Patterns);
    }
}
