using System.Text.Json;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Domain.Social.SingleAnalyzer;

public sealed class PatternSetTests
{
    // --- PatternItem invariants ---

    [Fact]
    public void PatternItem_WithValidFields_Succeeds()
    {
        var item = PatternItem.Create("Storytelling", "Uses hero arc", 0.9);
        Assert.True(item.IsSuccess);
        Assert.Equal("Storytelling", item.Value.Name);
        Assert.Equal("Uses hero arc", item.Value.Evidence);
        Assert.Equal(0.9, item.Value.Confidence);
    }

    [Fact]
    public void PatternItem_EmptyName_Fails()
    {
        var item = PatternItem.Create("", "some evidence", 0.5);
        Assert.True(item.IsFailure);
        Assert.Equal("social.single_analyzer.pattern_item_invalid", item.Error.Code);
    }

    [Fact]
    public void PatternItem_WhitespaceName_Fails()
    {
        var item = PatternItem.Create("   ", "some evidence", 0.5);
        Assert.True(item.IsFailure);
    }

    [Fact]
    public void PatternItem_EmptyEvidence_Fails()
    {
        var item = PatternItem.Create("Name", "", 0.5);
        Assert.True(item.IsFailure);
        Assert.Equal("social.single_analyzer.pattern_item_invalid", item.Error.Code);
    }

    [Fact]
    public void PatternItem_ConfidenceBelowZero_Fails()
    {
        var item = PatternItem.Create("Name", "Evidence", -0.01);
        Assert.True(item.IsFailure);
        Assert.Equal("social.single_analyzer.pattern_item_invalid", item.Error.Code);
    }

    [Fact]
    public void PatternItem_ConfidenceAboveOne_Fails()
    {
        var item = PatternItem.Create("Name", "Evidence", 1.01);
        Assert.True(item.IsFailure);
        Assert.Equal("social.single_analyzer.pattern_item_invalid", item.Error.Code);
    }

    [Fact]
    public void PatternItem_ConfidenceAtBoundary_ZeroAndOne_Succeeds()
    {
        var atZero = PatternItem.Create("Name", "Evidence", 0.0);
        var atOne = PatternItem.Create("Name", "Evidence", 1.0);

        Assert.True(atZero.IsSuccess);
        Assert.True(atOne.IsSuccess);
    }

    // --- PatternSet construction ---

    [Fact]
    public void PatternSet_Create_WithValidArrays_Succeeds()
    {
        var comm = new PatternItem[] { PatternItem.Create("Storytelling", "Hero arc", 0.9).Value };
        var emot = new PatternItem[] { PatternItem.Create("Urgency", "Deadline language", 0.7).Value };
        var conv = new PatternItem[] { PatternItem.Create("CTA clarity", "Clear next step", 0.8).Value };

        var result = PatternSet.Create(comm, emot, conv);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.CommunicationPatterns);
        Assert.Single(result.Value.EmotionalPatterns);
        Assert.Single(result.Value.ConversionPatterns);
    }

    [Fact]
    public void PatternSet_Create_WithAllEmptyArrays_Succeeds_EmptyPatternSetIsValid()
    {
        var result = PatternSet.Create(
            Array.Empty<PatternItem>(),
            Array.Empty<PatternItem>(),
            Array.Empty<PatternItem>());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.CommunicationPatterns);
        Assert.Empty(result.Value.EmotionalPatterns);
        Assert.Empty(result.Value.ConversionPatterns);
    }

    [Fact]
    public void PatternSet_Create_WithNullCommunicationArray_Fails()
    {
        var result = PatternSet.Create(null!, Array.Empty<PatternItem>(), Array.Empty<PatternItem>());

        Assert.True(result.IsFailure);
        Assert.Equal("social.single_analyzer.pattern_set_invalid", result.Error.Code);
    }

    [Fact]
    public void PatternSet_Create_WithNullEmotionalArray_Fails()
    {
        var result = PatternSet.Create(Array.Empty<PatternItem>(), null!, Array.Empty<PatternItem>());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void PatternSet_Create_WithNullConversionArray_Fails()
    {
        var result = PatternSet.Create(Array.Empty<PatternItem>(), Array.Empty<PatternItem>(), null!);

        Assert.True(result.IsFailure);
    }

    // --- PatternSet.Empty ---

    [Fact]
    public void PatternSet_Empty_AllCollectionsEmpty()
    {
        var empty = PatternSet.Empty;

        Assert.Empty(empty.CommunicationPatterns);
        Assert.Empty(empty.EmotionalPatterns);
        Assert.Empty(empty.ConversionPatterns);
    }

    // --- Equality ---

    [Fact]
    public void PatternSet_EqualityByValue_SameContent()
    {
        var item = PatternItem.Create("Hook", "Leads with question", 0.85).Value;
        var emptyItems = Array.Empty<PatternItem>();

        var a = PatternSet.Create(new[] { item }, emptyItems, emptyItems).Value;
        var b = PatternSet.Create(new[] { item }, emptyItems, emptyItems).Value;

        Assert.Equal(a, b);
    }

    [Fact]
    public void PatternSet_EqualityByValue_DifferentContent_NotEqual()
    {
        var itemA = PatternItem.Create("Hook", "Leads with question", 0.85).Value;
        var itemB = PatternItem.Create("CTA", "Direct offer", 0.6).Value;
        var emptyItems = Array.Empty<PatternItem>();

        var a = PatternSet.Create(new[] { itemA }, emptyItems, emptyItems).Value;
        var b = PatternSet.Create(new[] { itemB }, emptyItems, emptyItems).Value;

        Assert.NotEqual(a, b);
    }

    // --- Collections are read-only ---

    [Fact]
    public void PatternSet_Collections_AreReadOnly()
    {
        var item = PatternItem.Create("Hook", "Leads with question", 0.85).Value;
        var emptyItems = Array.Empty<PatternItem>();
        var set = PatternSet.Create(new[] { item }, emptyItems, emptyItems).Value;

        Assert.IsAssignableFrom<IReadOnlyList<PatternItem>>(set.CommunicationPatterns);
    }

    [Fact]
    public void SingleAnalyzerAnalysis_WithPatternSet_DeserializesFromAiJson()
    {
        const string json = """
            {
              "summary": "Resumen",
              "detectedHooks": ["Hook"],
              "likelyAudience": "Audience",
              "narrativeStructure": "Problem solution",
              "strengths": ["Strong hook"],
              "weaknesses": ["Needs shorter CTA"],
              "derivedIdeas": [
                {
                  "title": "Idea",
                  "concept": "Concept",
                  "rationale": "Rationale",
                  "score": {
                    "novelty": 4,
                    "clientFit": 4,
                    "platformFit": 4,
                    "hookQuality": 4,
                    "feasibility": 4,
                    "expectedEngagement": 4
                  }
                }
              ],
              "clientAdaptationRecommendations": ["Adapt"],
              "confidence": 0.86,
              "warnings": [],
              "patterns": {
                "communicationPatterns": [
                  {
                    "name": "Open loop",
                    "evidence": "Starts with a curiosity gap",
                    "confidence": 0.91
                  }
                ],
                "emotionalPatterns": [],
                "conversionPatterns": []
              }
            }
            """;

        var parsed = JsonSerializer.Deserialize<SingleAnalyzerAnalysis>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(parsed);
        Assert.NotNull(parsed!.Patterns);
        Assert.True(parsed.Validate().IsSuccess);
        Assert.Equal("Open loop", parsed.Patterns!.CommunicationPatterns[0].Name);
    }
}
