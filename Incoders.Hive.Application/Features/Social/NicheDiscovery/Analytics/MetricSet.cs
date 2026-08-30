namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.Analytics;

/// <summary>
/// The full deterministic metric surface for one <c>EvidenceSet</c> (design §6). Per-video and
/// per-channel metrics are keyed by <c>ExternalRef</c>; <see cref="GrowthProxy"/>,
/// <see cref="CrossChannelRepetition"/> and <see cref="EvergreenShare"/> are computed once over the
/// whole set passed to <see cref="DeterministicMetricCalculator.Compute"/> — a caller that wants
/// per-cluster values calls <c>Compute</c> again with a filtered <c>EvidenceSet</c>.
/// </summary>
public sealed record MetricSet(
    IReadOnlyDictionary<string, Metric> ChannelMedianViewsByChannel,
    IReadOnlyDictionary<string, Metric> OutlierRatioByVideo,
    IReadOnlyDictionary<string, Metric> OutlierDensityByChannel,
    IReadOnlyDictionary<string, Metric> VelocityProxyByVideo,
    Metric GrowthProxy,
    Metric CrossChannelRepetition,
    Metric EvergreenShare);
