namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;

/// <summary>
/// The availability-aware weighted-average mechanism (design §7.1): <c>score = Σ(wᵢ·sᵢ over AVAILABLE
/// i) / Σ(wᵢ over AVAILABLE i)</c>. This is a DELIBERATE re-declaration (~25 lines) of
/// <c>Social.ContentGeneration.Discovery.OpportunityScorer</c>'s <c>Accumulate</c>/<c>NormalizeWithinSet</c>
/// idiom, not an import across capabilities (N2, design §14) — the parity test in
/// <c>AvailabilityWeightedAggregateTests</c> is the anti-drift measure that duplication decision requires.
/// </summary>
public static class AvailabilityWeightedAggregate
{
    public static double Score(IReadOnlyList<(double Weight, double? Value)> weightedSignals)
    {
        double numerator = 0;
        double denominator = 0;

        foreach (var (weight, value) in weightedSignals)
        {
            if (value is null)
            {
                continue;
            }

            numerator += weight * value.Value;
            denominator += weight;
        }

        // A degenerate set — every signal unavailable — yields 0.5 rather than a divide-by-zero,
        // mirroring OpportunityScorer.Score's own defensive fallback.
        return denominator > 0 ? numerator / denominator : 0.5;
    }

    public static double EffectiveWeightDenominator(IReadOnlyList<(double Weight, double? Value)> weightedSignals) =>
        weightedSignals.Where(s => s.Value is not null).Sum(s => s.Weight);

    public static IReadOnlyList<double?> NormalizeWithinSet(IReadOnlyList<double?> raw)
    {
        var available = raw.Where(v => v.HasValue).Select(v => v!.Value).ToList();

        if (available.Count == 0)
        {
            return raw;
        }

        var min = available.Min();
        var max = available.Max();

        if (available.Count == 1 || Math.Abs(max - min) < double.Epsilon)
        {
            return raw.Select(v => v.HasValue ? (double?)0.5 : null).ToList();
        }

        return raw.Select(v => v.HasValue ? (double?)((v.Value - min) / (max - min)) : null).ToList();
    }
}
