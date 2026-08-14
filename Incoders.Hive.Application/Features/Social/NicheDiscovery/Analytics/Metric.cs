namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.Analytics;

/// <summary>
/// Structurally impossible to construct outside <see cref="DeterministicMetricCalculator"/> (design §3,
/// N3, guard 1): a private constructor plus exactly one internal factory. <c>Value</c> is <c>null</c>
/// when the underlying evidence does not support the computation (never zero-filled).
/// </summary>
public sealed record Metric
{
    private Metric(MetricKey key, double? value, int sampleSize)
    {
        Key = key;
        Value = value;
        SampleSize = sampleSize;
    }

    public MetricKey Key { get; }

    public double? Value { get; }

    public int SampleSize { get; }

    /// <summary>The ONLY way to obtain a <see cref="Metric"/> — asserted by a reflection/source guard
    /// test to have exactly one calling type, <see cref="DeterministicMetricCalculator"/>.</summary>
    internal static Metric From(MetricKey key, double? value, int sampleSize) => new(key, value, sampleSize);
}
