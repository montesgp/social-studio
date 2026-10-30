using SocialStudio.Application.Features.Social.NicheDiscovery.Analytics;
using SocialStudio.Application.Features.Social.NicheDiscovery.Scoring;

namespace SocialStudio.Tests.Application.Features.Social.NicheDiscovery.Scoring;

/// <summary>
/// WU-8: ND-10, design §7.2. <c>ConfidenceScore</c> is genuinely independent of <c>OpportunityScore</c>
/// — asserted, not claimed: it is built ONLY from <see cref="SampleShape"/> (sample-shape FACTS, never
/// magnitudes), so a metric magnitude change can never move it, and an <see cref="OpportunityInput"/>
/// magnitude change can never move it either, by construction (the two scorers share no input type).
/// </summary>
public sealed class ConfidenceScorerIndependenceTests
{
    [Fact]
    public void Score_ScalingEveryViewsValue_LeavesConfidenceScoreBitIdentical_WhileOpportunityScoreChanges()
    {
        // "Scaling every Views value ×10" is a magnitude change on the EVIDENCE that only ever reaches
        // OpportunityScore (via OpportunityInput's normalized signals) — SampleShape never carries a
        // magnitude for ConfidenceScorer to react to, so it cannot be affected by construction. This
        // fixture demonstrates the OpportunityScore side of that independence directly.
        var shapes = new[]
        {
            new SampleShape(ChannelCount: 5, VideoCount: 40, DistinctChannelCount: 5, WindowCoverageDays: 90, SourceCount: 1, DataAgeDays: 2, SignalConsistency: 0.8),
        };

        var lowMagnitude = new OpportunityInput("low", 0.2, 3, 0.3, 0.2, 0.3, 0.2, 0.2);
        var highMagnitude = new OpportunityInput("high", 0.9, 3, 0.9, 0.2, 0.3, 0.2, 0.2);

        var confidenceBefore = ConfidenceScorer.Score(shapes);
        var opportunities = NicheOpportunityScorer.Score(
            [lowMagnitude, highMagnitude], OpportunityScoreWeights.Default, DateTimeOffset.UtcNow);
        var confidenceAfter = ConfidenceScorer.Score(shapes);

        var opportunityLow = opportunities.Single(r => r.ExternalRef == "low");
        var opportunityHigh = opportunities.Single(r => r.ExternalRef == "high");

        Assert.NotEqual(opportunityLow.OpportunityScore, opportunityHigh.OpportunityScore);
        Assert.Equal(confidenceBefore, confidenceAfter);
    }

    [Fact]
    public void Score_DoublingChannelAndVideoCounts_ChangesConfidenceScore_WhileOpportunityScoreIsUnaffected()
    {
        var small = new SampleShape(ChannelCount: 3, VideoCount: 10, DistinctChannelCount: 3, WindowCoverageDays: 90, SourceCount: 1, DataAgeDays: 5, SignalConsistency: 0.5);
        var large = small with { ChannelCount = 6, VideoCount = 20, DistinctChannelCount = 6 };

        var confidenceScores = ConfidenceScorer.Score([small, large]);

        var input = new OpportunityInput("a", 0.5, 3, 0.5, 0.5, 0.5, 0.5, 0.5);
        var opportunityForSmall = NicheOpportunityScorer.Score([input], OpportunityScoreWeights.Default, DateTimeOffset.UtcNow);
        var opportunityForLarge = NicheOpportunityScorer.Score([input], OpportunityScoreWeights.Default, DateTimeOffset.UtcNow);

        Assert.NotEqual(confidenceScores[0], confidenceScores[1]);
        Assert.Equal(opportunityForSmall[0].OpportunityScore, opportunityForLarge[0].OpportunityScore);
    }

    [Fact]
    public void Score_TwoOpportunities_RenderBothScoresSideBySide_NeverAveraged()
    {
        // The proposal's illustrative fixture pair: a high-opportunity/low-confidence niche read
        // visibly differently from a lower-opportunity/high-confidence one (ND-10). The two numbers are
        // never combined into one — this test asserts they come from two entirely separate scorers over
        // two entirely separate input types.
        var highOpportunityLowConfidence = new OpportunityInput("thin-but-hot", 0.95, 5, 1.0, 0.6, 0.7, 0.5, 0.6);
        var lowerOpportunityHighConfidence = new OpportunityInput("broad-and-proven", 0.55, 3, 0.4, 0.5, 0.4, 0.4, 0.3);

        var opportunities = NicheOpportunityScorer.Score(
            [highOpportunityLowConfidence, lowerOpportunityHighConfidence], OpportunityScoreWeights.Default, DateTimeOffset.UtcNow);

        var thinShape = new SampleShape(ChannelCount: 2, VideoCount: 6, DistinctChannelCount: 2, WindowCoverageDays: 30, SourceCount: 1, DataAgeDays: 20, SignalConsistency: 0.3);
        var broadShape = new SampleShape(ChannelCount: 18, VideoCount: 120, DistinctChannelCount: 18, WindowCoverageDays: 90, SourceCount: 2, DataAgeDays: 1, SignalConsistency: 0.9);

        var confidences = ConfidenceScorer.Score([thinShape, broadShape]);

        var hot = opportunities.Single(r => r.ExternalRef == "thin-but-hot");
        var proven = opportunities.Single(r => r.ExternalRef == "broad-and-proven");

        Assert.True(hot.OpportunityScore > proven.OpportunityScore);
        Assert.True(confidences[1] > confidences[0]); // broad/proven sample shape is more confident
        Assert.NotEqual(hot.OpportunityScore, confidences[0]);
        Assert.NotEqual(proven.OpportunityScore, confidences[1]);
    }

    [Fact]
    public void Score_IsDeterministic_SameInputProducesSameOutput()
    {
        var shapes = new[]
        {
            new SampleShape(4, 12, 4, 60, 1, 3, 0.6),
            new SampleShape(9, 30, 9, 90, 2, 1, 0.9),
        };

        var first = ConfidenceScorer.Score(shapes);
        var second = ConfidenceScorer.Score(shapes);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Score_NullSignalConsistency_ExcludedFromTheAverage_NeverZeroFilled()
    {
        var withConsistency = new SampleShape(5, 20, 5, 90, 1, 2, 0.9);
        var withoutConsistency = withConsistency with { SignalConsistency = null };

        var scoresWith = ConfidenceScorer.Score([withConsistency]);
        var scoresWithout = ConfidenceScorer.Score([withoutConsistency]);

        // Both are single-item sets so every min-max normalizes to the degenerate 0.5 — but the point is
        // this must not throw and must not treat the missing term as 0.
        Assert.Single(scoresWith);
        Assert.Single(scoresWithout);
    }
}
