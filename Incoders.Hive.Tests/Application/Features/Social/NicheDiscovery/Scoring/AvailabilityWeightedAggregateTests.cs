using Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;

namespace Incoders.Hive.Tests.Application.Features.Social.NicheDiscovery.Scoring;

/// <summary>
/// WU-8: ND-09, design §7.1. <c>AvailabilityWeightedAggregate</c> re-declares (rather than imports)
/// <c>Social.ContentGeneration.Discovery.OpportunityScorer</c>'s renormalizing weighted-average
/// mechanism (N2) — this parity test is the anti-drift measure the duplication decision requires.
/// </summary>
public sealed class AvailabilityWeightedAggregateTests
{
    [Fact]
    public void Score_AllSignalsAvailable_RenormalizesAcrossTheFullWeightTable()
    {
        var signals = new (double Weight, double? Value)[]
        {
            (0.20, 1.0),
            (0.15, 0.5),
            (0.15, 0.0),
        };

        var score = AvailabilityWeightedAggregate.Score(signals);

        // Hand-computed: (0.20*1.0 + 0.15*0.5 + 0.15*0.0) / (0.20+0.15+0.15) = 0.275 / 0.50 = 0.55
        Assert.Equal(0.55, score, 10);
    }

    [Fact]
    public void Score_SomeSignalsUnavailable_RenormalizesTheDenominatorToOnlyAvailableWeights()
    {
        var signals = new (double Weight, double? Value)[]
        {
            (0.20, 1.0),
            (0.15, null), // unavailable — excluded from BOTH numerator and denominator
            (0.10, 0.5),
        };

        var score = AvailabilityWeightedAggregate.Score(signals);

        // Hand-computed: (0.20*1.0 + 0.10*0.5) / (0.20+0.10) = 0.25 / 0.30 = 0.8333...
        Assert.Equal(0.25 / 0.30, score, 10);
    }

    [Fact]
    public void Score_DegenerateSet_AllSignalsUnavailable_ReturnsPointFive()
    {
        var signals = new (double Weight, double? Value)[]
        {
            (0.20, null),
            (0.15, null),
        };

        var score = AvailabilityWeightedAggregate.Score(signals);

        Assert.Equal(0.5, score);
    }

    [Fact]
    public void Score_IsDeterministic_SameInputProducesSameOutput()
    {
        var signals = new (double Weight, double? Value)[] { (0.20, 0.73), (0.10, 0.12) };

        var first = AvailabilityWeightedAggregate.Score(signals);
        var second = AvailabilityWeightedAggregate.Score(signals);

        Assert.Equal(first, second);
    }

    [Fact]
    public void EffectiveWeightDenominator_SumsOnlyTheAvailableSignalWeights()
    {
        var signals = new (double Weight, double? Value)[]
        {
            (0.20, 1.0),
            (0.15, null),
            (0.10, 0.5),
        };

        var denominator = AvailabilityWeightedAggregate.EffectiveWeightDenominator(signals);

        Assert.Equal(0.30, denominator, 10);
    }

    [Fact]
    public void NormalizeWithinSet_MinMaxNormalizesAcrossAvailableValues()
    {
        var raw = new double?[] { 10.0, 20.0, 30.0, null };

        var normalized = AvailabilityWeightedAggregate.NormalizeWithinSet(raw);

        Assert.Equal(0.0, normalized[0]!.Value, 10);
        Assert.Equal(0.5, normalized[1]!.Value, 10);
        Assert.Equal(1.0, normalized[2]!.Value, 10);
        Assert.Null(normalized[3]);
    }

    [Fact]
    public void NormalizeWithinSet_DegenerateSet_SingleAvailableValue_ReturnsPointFive()
    {
        var raw = new double?[] { 42.0, null };

        var normalized = AvailabilityWeightedAggregate.NormalizeWithinSet(raw);

        Assert.Equal(0.5, normalized[0]);
        Assert.Null(normalized[1]);
    }
}
