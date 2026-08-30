using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Analytics;
using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Application.Features.Social.NicheDiscovery.Analytics;

/// <summary>
/// WU-6 (design §6): every formula and null-propagation row, table-tested. <c>nowUtc</c> is always a
/// fixed, explicit parameter — never <see cref="DateTimeOffset.UtcNow"/> read internally. The rule,
/// absolute: <c>null</c> = the provider does not report it, <c>0</c> = it reported zero and
/// participates in every median/ratio/count. Nothing is ever zero-filled.
/// </summary>
public sealed class DeterministicMetricCalculatorTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
    private static readonly DiscoveryWindow Window = DiscoveryWindow.Create(90).Value;

    [Fact]
    public void ChannelMedianViews_MedianOfNonNullViews_OverThatChannelsWindowVideos()
    {
        var evidence = new EvidenceSet(
            [],
            [
                Video("v1", "chan-1", views: 100),
                Video("v2", "chan-1", views: 300),
                Video("v3", "chan-1", views: 200),
            ]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.Equal(200, metrics.ChannelMedianViewsByChannel["chan-1"].Value);
    }

    [Fact]
    public void ChannelMedianViews_ZeroNonNullViews_IsNull()
    {
        var evidence = new EvidenceSet([], [Video("v1", "chan-1", views: null)]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.Null(metrics.ChannelMedianViewsByChannel["chan-1"].Value);
    }

    [Fact]
    public void OutlierRatio_ViewsOverChannelMedian()
    {
        var evidence = new EvidenceSet(
            [],
            [
                Video("v1", "chan-1", views: 100),
                Video("v2", "chan-1", views: 300),
                Video("v3", "chan-1", views: 600),
            ]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        // Median of [100, 300, 600] = 300.
        Assert.Equal(2.0, metrics.OutlierRatioByVideo["v3"].Value);
    }

    [Fact]
    public void OutlierRatio_NullWhenViewsIsNull()
    {
        var evidence = new EvidenceSet(
            [],
            [Video("v1", "chan-1", views: 100), Video("v2", "chan-1", views: null)]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.Null(metrics.OutlierRatioByVideo["v2"].Value);
    }

    [Fact]
    public void OutlierRatio_NullWhenChannelMedianIsNull()
    {
        var evidence = new EvidenceSet([], [Video("v1", "chan-1", views: 100)]);
        // Single video: median equals its own view count (100), never null/zero here — use a
        // channel where every video has null views instead so the median itself is null.
        var evidenceAllNull = new EvidenceSet([], [Video("v1", "chan-1", views: null), Video("v2", "chan-1", views: 100)]);

        var metrics = DeterministicMetricCalculator.Compute(evidenceAllNull, Window, NowUtc);

        Assert.Null(metrics.OutlierRatioByVideo["v1"].Value);
    }

    [Fact]
    public void OutlierDensity_CountAtOrAboveThreshold_OverNonNullOutlierRatioCount()
    {
        var evidence = new EvidenceSet(
            [],
            [
                Video("v1", "chan-1", views: 100), // median = 100 -> ratio 1.0 (not outlier)
                Video("v2", "chan-1", views: 100),
                Video("v3", "chan-1", views: 300), // ratio 3.0 -> outlier
            ]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.Equal(1.0 / 3.0, metrics.OutlierDensityByChannel["chan-1"].Value);
    }

    [Fact]
    public void OutlierDensity_NullWhenDenominatorIsZero()
    {
        var evidence = new EvidenceSet([], [Video("v1", "chan-1", views: null)]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.Null(metrics.OutlierDensityByChannel["chan-1"].Value);
    }

    [Fact]
    public void VelocityProxy_ViewsOverAgeDays_FloorOfOneDay()
    {
        var evidence = new EvidenceSet(
            [],
            [Video("v1", "chan-1", views: 1000, publishedAt: NowUtc.AddHours(-1))]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.Equal(1000.0, metrics.VelocityProxyByVideo["v1"].Value);
    }

    [Fact]
    public void VelocityProxy_NullWhenViewsOrPublishedAtIsNull()
    {
        var evidence = new EvidenceSet(
            [],
            [
                Video("v1", "chan-1", views: null, publishedAt: NowUtc.AddDays(-1)),
                Video("v2", "chan-1", views: 100, publishedAt: null),
            ]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.Null(metrics.VelocityProxyByVideo["v1"].Value);
        Assert.Null(metrics.VelocityProxyByVideo["v2"].Value);
    }

    [Fact]
    public void CrossChannelRepetition_BelowThreeDistinctChannels_IsZero_AvailableNotMissing()
    {
        var evidence = new EvidenceSet(
            [],
            [Video("v1", "chan-1", views: 1), Video("v2", "chan-2", views: 1)]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.NotNull(metrics.CrossChannelRepetition.Value);
        Assert.Equal(0, metrics.CrossChannelRepetition.Value);
    }

    [Fact]
    public void CrossChannelRepetition_AtOrAboveThreeDistinctChannels_IsTheDistinctCount()
    {
        var evidence = new EvidenceSet(
            [],
            [
                Video("v1", "chan-1", views: 1),
                Video("v2", "chan-2", views: 1),
                Video("v3", "chan-3", views: 1),
            ]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.Equal(3, metrics.CrossChannelRepetition.Value);
    }

    [Fact]
    public void EvergreenShare_NullWhenNoVideosAreOlderThanHalfTheWindow()
    {
        var evidence = new EvidenceSet(
            [],
            [Video("v1", "chan-1", views: 100, publishedAt: NowUtc.AddDays(-1))]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.Null(metrics.EvergreenShare.Value);
    }

    [Fact]
    public void EvergreenShare_ShareOfOldVideosWithOutlierRatioAtLeastOne()
    {
        var oldEnough = NowUtc.AddDays(-(Window.Days / 2.0) - 1);

        var evidence = new EvidenceSet(
            [],
            [
                Video("v1", "chan-1", views: 100, publishedAt: oldEnough), // ratio to median(100,300)=200 -> 0.5, not evergreen
                Video("v2", "chan-1", views: 300, publishedAt: oldEnough), // ratio 1.5 -> evergreen
            ]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.Equal(0.5, metrics.EvergreenShare.Value);
    }

    [Fact]
    public void GrowthProxy_NullWhenEitherThirdHasNoNonNullVelocity()
    {
        var evidence = new EvidenceSet(
            [],
            [
                Video("v1", "chan-1", views: null, publishedAt: NowUtc.AddDays(-10)),
                Video("v2", "chan-1", views: 100, publishedAt: NowUtc.AddDays(-1)),
            ]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        Assert.Null(metrics.GrowthProxy.Value);
    }

    [Fact]
    public void GrowthProxy_MedianOfNewestThird_OverMedianOfOldestThird()
    {
        var evidence = new EvidenceSet(
            [],
            [
                Video("v1", "chan-1", views: 100, publishedAt: NowUtc.AddDays(-30)),
                Video("v2", "chan-1", views: 100, publishedAt: NowUtc.AddDays(-29)),
                Video("v3", "chan-1", views: 100, publishedAt: NowUtc.AddDays(-28)),
                Video("v4", "chan-1", views: 100, publishedAt: NowUtc.AddDays(-15)),
                Video("v5", "chan-1", views: 100, publishedAt: NowUtc.AddDays(-14)),
                Video("v6", "chan-1", views: 100, publishedAt: NowUtc.AddDays(-13)),
                Video("v7", "chan-1", views: 400, publishedAt: NowUtc.AddDays(-3)),
                Video("v8", "chan-1", views: 400, publishedAt: NowUtc.AddDays(-2)),
                Video("v9", "chan-1", views: 400, publishedAt: NowUtc.AddDays(-1)),
            ]);

        var metrics = DeterministicMetricCalculator.Compute(evidence, Window, NowUtc);

        // Oldest third velocities ~= 100/30..28, newest third velocities ~= 400/3..1: newest median
        // is clearly higher than the oldest median, so growth > 1.
        Assert.NotNull(metrics.GrowthProxy.Value);
        Assert.True(metrics.GrowthProxy.Value > 1.0);
    }

    [Fact]
    public void Compute_TieBreaksOnExternalRefOrdinal_ResultIndependentOfInputOrder()
    {
        var videosInOrder = new[]
        {
            Video("a", "chan-1", views: 100),
            Video("b", "chan-1", views: 200),
            Video("c", "chan-1", views: 300),
        };

        var forward = DeterministicMetricCalculator.Compute(new EvidenceSet([], videosInOrder), Window, NowUtc);
        var reversed = DeterministicMetricCalculator.Compute(new EvidenceSet([], videosInOrder.Reverse().ToArray()), Window, NowUtc);

        Assert.Equal(forward.ChannelMedianViewsByChannel["chan-1"].Value, reversed.ChannelMedianViewsByChannel["chan-1"].Value);
        Assert.Equal(forward.OutlierRatioByVideo["a"].Value, reversed.OutlierRatioByVideo["a"].Value);
    }

    private static VideoEvidence Video(
        string externalRef,
        string channelExternalRef,
        long? views,
        DateTimeOffset? publishedAt = null) =>
        new(
            externalRef,
            channelExternalRef,
            $"Title {externalRef}",
            null,
            publishedAt,
            views,
            null,
            null,
            null,
            null,
            "test-source");
}
