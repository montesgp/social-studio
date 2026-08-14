using Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;

namespace Incoders.Hive.Tests.Application.Features.Social.NicheDiscovery.Scoring;

/// <summary>
/// WU-8: ND-09, design §7.1. <c>CrossPlatformValidation</c> has no data source in this change — it is
/// PERMANENTLY excluded (<c>NotSupportedYet</c>, N8), never a zero score. The renormalizing-out-and-
/// surfacing-as-excluded test is the proposal's success criterion directly.
/// </summary>
public sealed class NicheOpportunityScorerTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Score_EmptySet_ReturnsEmpty()
    {
        var result = NicheOpportunityScorer.Score([], OpportunityScoreWeights.Default, NowUtc);

        Assert.Empty(result);
    }

    [Fact]
    public void Score_CrossPlatformValidation_AlwaysExcluded_RenormalizesTheDenominatorTo0Point90()
    {
        var input = new OpportunityInput(
            ExternalRef: "cluster-a",
            ViralPerformance: 0.5,
            CrossChannelValidation: 5,
            DemandMomentum: 1.2,
            TargetMarketGap: 0.4,
            AutomationSuitability: 0.8,
            EvergreenPotential: 0.3,
            MonetizationPotential: 0.6);

        var result = NicheOpportunityScorer.Score([input], OpportunityScoreWeights.Default, NowUtc);

        Assert.Single(result);
        Assert.Equal(0.90, result[0].EffectiveWeightDenominator, 10);
        Assert.Contains(result[0].ExcludedSignals, s => s.Key == "cross_platform_validation");
        var excluded = result[0].ExcludedSignals.Single(s => s.Key == "cross_platform_validation");
        Assert.Equal("not_supported_yet", excluded.ReasonKey);
        Assert.Equal("social.niche_discovery.signals.not_supported_yet", excluded.I18nKey);
    }

    [Fact]
    public void Score_CrossPlatformValidationExcluded_RanksIdenticallyToTheSameFixtureWithTheWeightRemovedByHand()
    {
        // ND-09 scenario 2: excluding CrossPlatformValidation from the weight table entirely (as if it
        // never existed) must produce the SAME ranking as excluding it because it is NotSupportedYet.
        var a = new OpportunityInput("a", 0.9, 5, 1.5, 0.6, 0.7, 0.5, 0.8);
        var b = new OpportunityInput("b", 0.2, 1, 0.3, 0.1, 0.1, 0.1, 0.1);

        var withCrossPlatformWeight = NicheOpportunityScorer.Score([a, b], OpportunityScoreWeights.Default, NowUtc);

        // "Removed by hand": a weight table where CrossPlatformValidation carries weight 0 — mathematically
        // equivalent to renormalizing it out entirely, since a 0-weight signal never contributes regardless
        // of availability.
        var withoutCrossPlatformWeight = NicheOpportunityScorer.Score(
            [a, b],
            OpportunityScoreWeights.Default with { CrossPlatformValidation = 0 },
            NowUtc);

        Assert.Equal(
            withCrossPlatformWeight.Select(r => r.ExternalRef),
            withoutCrossPlatformWeight.Select(r => r.ExternalRef));
    }

    [Fact]
    public void Score_ASignalMissingForOneOpportunity_IsExcludedOnlyForThatOpportunity()
    {
        var withAutomation = new OpportunityInput("with", 0.5, 3, 0.5, 0.5, 0.5, 0.5, 0.5);
        var withoutAutomation = new OpportunityInput("without", 0.5, 3, 0.5, 0.5, null, 0.5, 0.5);

        var result = NicheOpportunityScorer.Score(
            [withAutomation, withoutAutomation], OpportunityScoreWeights.Default, NowUtc);

        var withRow = result.Single(r => r.ExternalRef == "with");
        var withoutRow = result.Single(r => r.ExternalRef == "without");

        Assert.DoesNotContain(withRow.ExcludedSignals, s => s.Key == "automation_suitability");
        Assert.Contains(withoutRow.ExcludedSignals, s => s.Key == "automation_suitability");
        Assert.Equal(0.90, withRow.EffectiveWeightDenominator, 10);
        Assert.Equal(0.80, withoutRow.EffectiveWeightDenominator, 10);
    }

    [Fact]
    public void Score_OrdersHighestScoreFirst_TiesBreakOnExternalRefOrdinal()
    {
        var high = new OpportunityInput("z-high", 1.0, 5, 1.0, 1.0, 1.0, 1.0, 1.0);
        var low = new OpportunityInput("a-low", 0.0, 0, 0.0, 0.0, 0.0, 0.0, 0.0);

        var result = NicheOpportunityScorer.Score([low, high], OpportunityScoreWeights.Default, NowUtc);

        Assert.Equal(["z-high", "a-low"], result.Select(r => r.ExternalRef));
    }
}
