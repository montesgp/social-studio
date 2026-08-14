using Incoders.Hive.Application.Features.Social.NicheDiscovery.Analytics;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.MarketComparison;

/// <summary>
/// Target-market demand (ND-08, design §4): the median <c>VelocityProxy</c> of a cluster's window
/// videos whose <c>LanguageCode</c> matches a market's language, normalized within the run set via the
/// same <see cref="AvailabilityWeightedAggregate.NormalizeWithinSet"/> mechanism the scorers already
/// use — so it is directly comparable to <see cref="TargetMarketCompetition"/> inside
/// <see cref="TargetMarketGap"/>.
///
/// <see cref="RawMedianVelocity"/> is also the formula behind <c>SourceMarketTraction</c>
/// (<see cref="TargetMarketEvaluator"/>): the SAME computation against the source market's language,
/// deliberately NOT run-normalized — traction describes one cluster's own strength, not its rank
/// against the rest of the run.
/// </summary>
public static class TargetMarketDemand
{
    public static IReadOnlyList<double?> Compute(IReadOnlyList<TargetMarketComparisonInput> clusters, string language)
    {
        var raw = clusters.Select(c => RawMedianVelocity(c, language)).ToList();
        return AvailabilityWeightedAggregate.NormalizeWithinSet(raw);
    }

    public static double? RawMedianVelocity(TargetMarketComparisonInput cluster, string language)
    {
        var velocities = cluster.ClusterEvidence.Videos
            .Where(v => string.Equals(v.LanguageCode, language, StringComparison.OrdinalIgnoreCase))
            .Select(v => cluster.ClusterMetrics.VelocityProxyByVideo.TryGetValue(v.ExternalRef, out var metric) ? metric.Value : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        return velocities.Count > 0 ? Median(velocities) : null;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;

        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }
}
