using SocialStudio.Application.Features.Social.NicheDiscovery.Analytics;

namespace SocialStudio.Application.Features.Social.NicheDiscovery.Scoring;

/// <summary>
/// Pure confidence scorer (ND-10, design §7.2). Built ONLY from <see cref="SampleShape"/> —
/// sample-shape FACTS, never magnitudes — which is what makes independence from
/// <see cref="NicheOpportunityScorer"/> a structural property rather than a coincidence: the two
/// scorers share no input type, so no code path exists by which a metric magnitude could move this
/// score, or a sample-shape fact could move <c>OpportunityScore</c>.
///
/// Each term is min-max normalized WITHIN the run and averaged with equal weight, reusing
/// <see cref="AvailabilityWeightedAggregate"/>'s renormalizing mechanism (weight 1 per term) so a
/// missing <see cref="SampleShape.SignalConsistency"/> is excluded from the average, never zero-filled.
/// <see cref="SampleShape.DataAgeDays"/> is the one term where LOWER is better (fresher data), so its
/// normalized value is inverted before averaging.
/// </summary>
public static class ConfidenceScorer
{
    public static IReadOnlyList<double> Score(IReadOnlyList<SampleShape> set)
    {
        ArgumentNullException.ThrowIfNull(set);

        if (set.Count == 0)
        {
            return [];
        }

        var channelCount = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(s => (double?)s.ChannelCount).ToList());
        var videoCount = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(s => (double?)s.VideoCount).ToList());
        var distinctChannelCount = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(s => (double?)s.DistinctChannelCount).ToList());
        var windowCoverageDays = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(s => (double?)s.WindowCoverageDays).ToList());
        var sourceCount = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(s => (double?)s.SourceCount).ToList());
        var dataAgeDays = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(s => (double?)s.DataAgeDays).ToList());
        var signalConsistency = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(s => s.SignalConsistency).ToList());

        var results = new List<double>(set.Count);

        for (var i = 0; i < set.Count; i++)
        {
            var weighted = new (double Weight, double? Value)[]
            {
                (1.0, channelCount[i]),
                (1.0, videoCount[i]),
                (1.0, distinctChannelCount[i]),
                (1.0, windowCoverageDays[i]),
                (1.0, sourceCount[i]),
                // Fresher data is MORE confident, so a low DataAgeDays raw value (normalized low)
                // inverts to a HIGH confidence contribution.
                (1.0, Invert(dataAgeDays[i])),
                (1.0, signalConsistency[i]),
            };

            results.Add(AvailabilityWeightedAggregate.Score(weighted));
        }

        return results;
    }

    private static double? Invert(double? normalized) => normalized is null ? null : 1.0 - normalized.Value;
}
