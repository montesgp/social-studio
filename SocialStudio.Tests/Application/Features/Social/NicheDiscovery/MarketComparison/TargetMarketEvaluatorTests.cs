using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Application.Features.Social.NicheDiscovery.Analytics;
using SocialStudio.Application.Features.Social.NicheDiscovery.MarketComparison;
using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Tests.Application.Features.Social.NicheDiscovery.MarketComparison;

/// <summary>
/// WU-9 (ND-08, design §4). Owner decision (task 9.1): target-market competition is partitioned by
/// CONTENT LANGUAGE + REGION, not by audience geography — see <see cref="TargetMarketEvaluator"/>'s
/// class doc for the stated trade-off.
/// </summary>
public sealed class TargetMarketEvaluatorTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DiscoveryWindow Window = DiscoveryWindow.Create(90).Value;
    private static readonly MarketCode SourceMarket = MarketCode.TryParse("pt-BR").Value;
    private static readonly MarketCode TargetMarket = MarketCode.TryParse("es-AR").Value;

    [Fact]
    public void Evaluate_EmptySet_ReturnsEmpty()
    {
        var result = TargetMarketEvaluator.Evaluate([], SourceMarket, TargetMarket);

        Assert.Empty(result);
    }

    [Fact]
    public void Evaluate_AllFourValues_AreIndividuallyInspectable()
    {
        var cluster = BuildCluster(
            "cluster-a",
            sourceVideos: [("v-pt-1", "chan-pt-1", 1000L)],
            targetVideos: [("v-es-1", "chan-es-1", 2000L)],
            targetChannels: [("chan-es-1", 50_000L)]);

        var result = TargetMarketEvaluator.Evaluate([cluster], SourceMarket, TargetMarket);

        Assert.Single(result);
        var evaluation = result[0];
        Assert.Equal("cluster-a", evaluation.ClusterKey);
        Assert.NotNull(evaluation.SourceMarketTraction);
        Assert.NotNull(evaluation.TargetMarketDemandValue);
        Assert.NotNull(evaluation.TargetMarketCompetitionValue);
        Assert.NotNull(evaluation.TargetMarketGapValue);
    }

    /// <summary>
    /// Normalized demand is RUN-RELATIVE: in a run where every cluster is weak, the least weak one
    /// still normalizes near 1.0, which reads as "high demand" for a market that may have no audience
    /// at all. The raw median velocity is the only value carrying absolute magnitude, and it is what
    /// any future absolute-viability rule ("below this, not worth building") has to compare against.
    /// </summary>
    [Fact]
    public void Evaluate_RawTargetDemand_IsNotNormalized_AndKeepsAbsoluteMagnitude()
    {
        var weak = BuildCluster(
            "weak",
            sourceVideos: [],
            targetVideos: [("v-es-weak", "chan-es-weak", 10L)],
            targetChannels: [("chan-es-weak", 1_000L)]);

        var weaker = BuildCluster(
            "weaker",
            sourceVideos: [],
            targetVideos: [("v-es-weaker", "chan-es-weaker", 1L)],
            targetChannels: [("chan-es-weaker", 1_000L)]);

        var result = TargetMarketEvaluator.Evaluate([weak, weaker], SourceMarket, TargetMarket);

        var best = result.Single(r => r.ClusterKey == "weak");

        // The normalized value tops out at 1.0 — "best of this run", not "strong in absolute terms".
        Assert.Equal(1.0, best.TargetMarketDemandValue);

        // The raw value keeps the real magnitude, so the two are distinguishable.
        Assert.NotNull(best.TargetMarketRawDemand);
        Assert.NotEqual(best.TargetMarketDemandValue, best.TargetMarketRawDemand);
        Assert.True(
            best.TargetMarketRawDemand > result.Single(r => r.ClusterKey == "weaker").TargetMarketRawDemand,
            "Raw demand must preserve the ordering of the underlying absolute velocities.");
    }

    /// <summary>An unmeasurable target market yields no raw value either — never zero-filled (ND-05).</summary>
    [Fact]
    public void Evaluate_RawTargetDemand_IsNull_WhenTheTargetMarketHasNoMeasurableVideos()
    {
        var cluster = BuildCluster(
            "source-only",
            sourceVideos: [("v-pt-1", "chan-pt-1", 1000L)],
            targetVideos: [],
            targetChannels: []);

        var result = TargetMarketEvaluator.Evaluate([cluster], SourceMarket, TargetMarket);

        Assert.Null(result[0].TargetMarketRawDemand);
        Assert.Null(result[0].TargetMarketDemandValue);
    }

    [Fact]
    public void Evaluate_Demand_NormalizedWithinTheRunSet_NotJustTheCluster()
    {
        var lowDemandCluster = BuildCluster(
            "low",
            sourceVideos: [],
            targetVideos: [("v-es-low", "chan-es-low", 100L)],
            targetChannels: [("chan-es-low", 10_000L)]);

        var highDemandCluster = BuildCluster(
            "high",
            sourceVideos: [],
            targetVideos: [("v-es-high", "chan-es-high", 100_000L)],
            targetChannels: [("chan-es-high", 10_000L)]);

        var result = TargetMarketEvaluator.Evaluate([lowDemandCluster, highDemandCluster], SourceMarket, TargetMarket);

        var low = result.Single(r => r.ClusterKey == "low");
        var high = result.Single(r => r.ClusterKey == "high");

        Assert.Equal(0.0, low.TargetMarketDemandValue);
        Assert.Equal(1.0, high.TargetMarketDemandValue);
    }

    [Fact]
    public void Evaluate_Competition_WeightsByChannelSubscriberDecile_AcrossTheWholeRun()
    {
        // A cluster whose only target-language channel sits at the bottom of the run's subscriber
        // range competes weaker than a cluster whose channel dominates it — decile is a RUN-wide rank,
        // not computed against each cluster's own (possibly tiny) channel set in isolation.
        var smallChannelCluster = BuildCluster(
            "small",
            sourceVideos: [],
            targetVideos: [("v-es-1", "chan-small", 100L)],
            targetChannels: [("chan-small", 1_000L)]);

        var bigChannelCluster = BuildCluster(
            "big",
            sourceVideos: [],
            targetVideos: [("v-es-2", "chan-big", 100L)],
            targetChannels: [("chan-big", 1_000_000L)]);

        var result = TargetMarketEvaluator.Evaluate([smallChannelCluster, bigChannelCluster], SourceMarket, TargetMarket);

        var small = result.Single(r => r.ClusterKey == "small");
        var big = result.Single(r => r.ClusterKey == "big");

        Assert.True(small.TargetMarketCompetitionValue < big.TargetMarketCompetitionValue);
    }

    [Fact]
    public void Evaluate_Gap_IsDemandTimesOneMinusCompetition()
    {
        var cluster = BuildCluster(
            "cluster-a",
            sourceVideos: [],
            targetVideos: [("v-es-1", "chan-es-1", 5000L)],
            targetChannels: [("chan-es-1", 10_000L)]);

        var evaluation = TargetMarketEvaluator.Evaluate([cluster], SourceMarket, TargetMarket)[0];

        Assert.Equal(
            evaluation.TargetMarketDemandValue!.Value * (1 - evaluation.TargetMarketCompetitionValue!.Value),
            evaluation.TargetMarketGapValue!.Value,
            10);
    }

    [Fact]
    public void Evaluate_Gap_IsNullWhenDemandIsNull()
    {
        // No target-language video at all in this cluster: there is nothing to compute demand from.
        var cluster = BuildCluster(
            "cluster-a",
            sourceVideos: [("v-pt-1", "chan-pt-1", 1000L)],
            targetVideos: [],
            targetChannels: [("chan-es-1", 10_000L)]);

        var evaluation = TargetMarketEvaluator.Evaluate([cluster], SourceMarket, TargetMarket)[0];

        Assert.Null(evaluation.TargetMarketDemandValue);
        Assert.Null(evaluation.TargetMarketGapValue);
    }

    [Fact]
    public void Evaluate_Gap_IsNullWhenCompetitionIsNull()
    {
        // A target-language video exists, but no channel evidence carries a subscriber count for that
        // language — competition cannot be assessed.
        var cluster = BuildCluster(
            "cluster-a",
            sourceVideos: [],
            targetVideos: [("v-es-1", "chan-es-1", 5000L)],
            targetChannels: []);

        var evaluation = TargetMarketEvaluator.Evaluate([cluster], SourceMarket, TargetMarket)[0];

        Assert.Null(evaluation.TargetMarketCompetitionValue);
        Assert.Null(evaluation.TargetMarketGapValue);
    }

    [Fact]
    public void Evaluate_SourceMarketTraction_OnlyCountsVideosMatchingSourceLanguage_AndIsNotRunNormalized()
    {
        var cluster = BuildCluster(
            "cluster-a",
            sourceVideos: [("v-pt-1", "chan-pt-1", 8000L)],
            targetVideos: [("v-es-1", "chan-es-1", 200L)],
            targetChannels: [("chan-es-1", 10_000L)]);

        var evaluation = TargetMarketEvaluator.Evaluate([cluster], SourceMarket, TargetMarket)[0];

        // Every fixture video is published exactly 1 day before NowUtc, so VelocityProxy == Views —
        // this is the raw, un-normalized source-language median, unlike TargetMarketDemandValue.
        Assert.Equal(8000.0, evaluation.SourceMarketTraction);
    }

    // ── fixture builder ──────────────────────────────────────────────────────

    private static TargetMarketComparisonInput BuildCluster(
        string clusterKey,
        IReadOnlyList<(string ExternalRef, string ChannelRef, long Views)> sourceVideos,
        IReadOnlyList<(string ExternalRef, string ChannelRef, long Views)> targetVideos,
        IReadOnlyList<(string ChannelRef, long Subscribers)> targetChannels)
    {
        var videos = sourceVideos
            .Select(v => Video(v.ExternalRef, v.ChannelRef, v.Views, SourceMarket.Language))
            .Concat(targetVideos.Select(v => Video(v.ExternalRef, v.ChannelRef, v.Views, TargetMarket.Language)))
            .ToList();

        var channels = targetChannels
            .Select(c => new ChannelEvidence(
                c.ChannelRef, c.ChannelRef, Country: null, c.Subscribers, TotalViews: null, VideoCount: null,
                TargetMarket.Language, CreatedAtUtc: null, ThumbnailUrl: null, Description: null, "youtube-data-api"))
            .ToList();

        var evidence = new EvidenceSet(channels, videos);
        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        return new TargetMarketComparisonInput(clusterKey, evidence, metrics);
    }

    private static VideoEvidence Video(string externalRef, string channelRef, long views, string language) => new(
        externalRef, channelRef, "Title", Description: null, NowUtc.AddDays(-1),
        views, Likes: null, Comments: null, DurationSeconds: null, language, "youtube-data-api");
}
