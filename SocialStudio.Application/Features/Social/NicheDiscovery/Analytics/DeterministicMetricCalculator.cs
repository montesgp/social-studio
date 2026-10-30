using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Application.Features.Social.NicheDiscovery.Analytics;

/// <summary>
/// Pure, I/O-free deterministic analytics (design §6), mirroring
/// <c>Social.ContentGeneration.Discovery.OpportunityScorer</c>'s "never <see cref="DateTimeOffset.UtcNow"/>
/// internally" discipline. The ONLY caller of <see cref="Metric.From"/> — asserted by a source-scan
/// guard test. <c>null</c> = the provider does not report it; <c>0</c> = it reported zero and
/// participates in every median/ratio/count. Nothing is ever zero-filled (ND-05).
/// </summary>
public static class DeterministicMetricCalculator
{
    private const double OutlierThreshold = 3.0;
    private const int CrossChannelRepetitionFloor = 3;

    public static MetricSet Compute(EvidenceSet evidence, DiscoveryWindow window, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        // Deterministic iteration order (ExternalRef ordinal), independent of the caller's input
        // order — the "total order independent of input order" rule (design §6).
        var videos = evidence.Videos.OrderBy(v => v.ExternalRef, StringComparer.Ordinal).ToList();
        var videosByChannel = videos
            .GroupBy(v => v.ChannelExternalRef, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var channelMedianViews = ComputeChannelMedianViews(videosByChannel);
        var outlierRatioByVideo = ComputeOutlierRatios(videos, channelMedianViews);
        var outlierDensityByChannel = ComputeOutlierDensity(videosByChannel, outlierRatioByVideo);
        var velocityByVideo = ComputeVelocityProxies(videos, nowUtc);
        var growthProxy = ComputeGrowthProxy(videos, velocityByVideo);
        var crossChannelRepetition = ComputeCrossChannelRepetition(videos);
        var evergreenShare = ComputeEvergreenShare(videos, window, nowUtc, outlierRatioByVideo);

        return new MetricSet(
            channelMedianViews,
            outlierRatioByVideo,
            outlierDensityByChannel,
            velocityByVideo,
            growthProxy,
            crossChannelRepetition,
            evergreenShare);
    }

    private static IReadOnlyDictionary<string, Metric> ComputeChannelMedianViews(
        IReadOnlyDictionary<string, List<VideoEvidence>> videosByChannel)
    {
        var result = new Dictionary<string, Metric>(StringComparer.Ordinal);

        foreach (var (channelRef, channelVideos) in videosByChannel)
        {
            var nonNullViews = channelVideos
                .Where(v => v.Views.HasValue)
                .Select(v => (double)v.Views!.Value)
                .ToList();

            var median = nonNullViews.Count > 0 ? Median(nonNullViews) : (double?)null;
            result[channelRef] = Metric.From(MetricKey.ChannelMedianViews, median, nonNullViews.Count);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, Metric> ComputeOutlierRatios(
        IReadOnlyList<VideoEvidence> videos,
        IReadOnlyDictionary<string, Metric> channelMedianViews)
    {
        var result = new Dictionary<string, Metric>(StringComparer.Ordinal);

        foreach (var video in videos)
        {
            double? ratio = null;

            if (video.Views.HasValue
                && channelMedianViews.TryGetValue(video.ChannelExternalRef, out var medianMetric)
                && medianMetric.Value is > 0)
            {
                ratio = video.Views.Value / medianMetric.Value.Value;
            }

            result[video.ExternalRef] = Metric.From(MetricKey.OutlierRatio, ratio, ratio.HasValue ? 1 : 0);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, Metric> ComputeOutlierDensity(
        IReadOnlyDictionary<string, List<VideoEvidence>> videosByChannel,
        IReadOnlyDictionary<string, Metric> outlierRatioByVideo)
    {
        var result = new Dictionary<string, Metric>(StringComparer.Ordinal);

        foreach (var (channelRef, channelVideos) in videosByChannel)
        {
            var ratios = ExtractRatios(channelVideos, outlierRatioByVideo);

            double? density = ratios.Count > 0
                ? ratios.Count(r => r >= OutlierThreshold) / (double)ratios.Count
                : null;

            result[channelRef] = Metric.From(MetricKey.OutlierDensity, density, ratios.Count);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, Metric> ComputeVelocityProxies(
        IReadOnlyList<VideoEvidence> videos, DateTimeOffset nowUtc)
    {
        var result = new Dictionary<string, Metric>(StringComparer.Ordinal);

        foreach (var video in videos)
        {
            double? velocity = null;

            if (video.Views.HasValue && video.PublishedAtUtc.HasValue)
            {
                var ageDays = Math.Max((nowUtc - video.PublishedAtUtc.Value).TotalDays, 1.0);
                velocity = video.Views.Value / ageDays;
            }

            result[video.ExternalRef] = Metric.From(MetricKey.VelocityProxy, velocity, velocity.HasValue ? 1 : 0);
        }

        return result;
    }

    private static Metric ComputeGrowthProxy(
        IReadOnlyList<VideoEvidence> videos, IReadOnlyDictionary<string, Metric> velocityByVideo)
    {
        var ordered = videos
            .Where(v => v.PublishedAtUtc.HasValue)
            .OrderBy(v => v.PublishedAtUtc!.Value)
            .ThenBy(v => v.ExternalRef, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count == 0)
        {
            return Metric.From(MetricKey.GrowthProxy, null, 0);
        }

        var thirdSize = Math.Max(ordered.Count / 3, 1);
        var oldestThird = ordered.Take(thirdSize).ToList();
        var newestThird = ordered.Skip(Math.Max(ordered.Count - thirdSize, 0)).ToList();

        var oldestVelocities = ExtractVelocities(oldestThird, velocityByVideo);
        var newestVelocities = ExtractVelocities(newestThird, velocityByVideo);

        if (oldestVelocities.Count == 0 || newestVelocities.Count == 0)
        {
            return Metric.From(MetricKey.GrowthProxy, null, oldestVelocities.Count + newestVelocities.Count);
        }

        var oldestMedian = Median(oldestVelocities);
        var newestMedian = Median(newestVelocities);

        // A zero (not merely absent) oldest median makes the ratio undefined rather than infinite —
        // treated as unavailable, same as every other divide-by-zero guard in this calculator.
        double? growth = oldestMedian > 0 ? newestMedian / oldestMedian : null;

        return Metric.From(MetricKey.GrowthProxy, growth, oldestVelocities.Count + newestVelocities.Count);
    }

    private static Metric ComputeCrossChannelRepetition(IReadOnlyList<VideoEvidence> videos)
    {
        var distinctChannelCount = videos
            .Select(v => v.ChannelExternalRef)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .Count();

        // Below K=3 the value is 0 — AVAILABLE, not missing (design §6). Never null.
        var value = distinctChannelCount < CrossChannelRepetitionFloor ? 0 : distinctChannelCount;

        return Metric.From(MetricKey.CrossChannelRepetition, value, videos.Count);
    }

    private static Metric ComputeEvergreenShare(
        IReadOnlyList<VideoEvidence> videos,
        DiscoveryWindow window,
        DateTimeOffset nowUtc,
        IReadOnlyDictionary<string, Metric> outlierRatioByVideo)
    {
        var halfWindowDays = window.Days / 2.0;

        var olderThanHalf = videos
            .Where(v => v.PublishedAtUtc.HasValue && (nowUtc - v.PublishedAtUtc.Value).TotalDays > halfWindowDays)
            .ToList();

        if (olderThanHalf.Count == 0)
        {
            return Metric.From(MetricKey.EvergreenShare, null, 0);
        }

        var evergreenCount = olderThanHalf.Count(v =>
            outlierRatioByVideo.TryGetValue(v.ExternalRef, out var ratioMetric) && ratioMetric.Value is >= 1.0);

        var share = evergreenCount / (double)olderThanHalf.Count;

        return Metric.From(MetricKey.EvergreenShare, share, olderThanHalf.Count);
    }

    private static List<double> ExtractRatios(
        IEnumerable<VideoEvidence> videos, IReadOnlyDictionary<string, Metric> outlierRatioByVideo) =>
        videos
            .Select(v => outlierRatioByVideo.TryGetValue(v.ExternalRef, out var m) ? m.Value : null)
            .Where(r => r.HasValue)
            .Select(r => r!.Value)
            .ToList();

    private static List<double> ExtractVelocities(
        IEnumerable<VideoEvidence> videos, IReadOnlyDictionary<string, Metric> velocityByVideo) =>
        videos
            .Select(v => velocityByVideo.TryGetValue(v.ExternalRef, out var m) ? m.Value : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;

        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }
}
