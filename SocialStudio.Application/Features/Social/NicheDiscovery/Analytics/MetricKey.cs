namespace SocialStudio.Application.Features.Social.NicheDiscovery.Analytics;

/// <summary>The seven deterministic metrics computed by <see cref="DeterministicMetricCalculator"/> (design §6).</summary>
public enum MetricKey
{
    ChannelMedianViews,
    OutlierRatio,
    OutlierDensity,
    VelocityProxy,
    GrowthProxy,
    CrossChannelRepetition,
    EvergreenShare,
}
