using SocialStudio.Application.Features.Social.NicheDiscovery.Scoring;

namespace SocialStudio.Application.Features.Social.NicheDiscovery.MarketComparison;

/// <summary>
/// Target-market competition (ND-08, design §4): distinct channels publishing in a market's language,
/// weighted by subscriber decile, normalized within the run set.
///
/// ACCEPTED TRADE-OFF, stated plainly (owner decision, task 9.1): competition is partitioned by
/// CONTENT LANGUAGE + REGION — the target <c>MarketCode</c> — not by audience geography. A channel
/// producing in <c>es-ES</c> that happens to reach an Argentine audience is NOT counted as competition
/// for <c>es-AR</c>; a same-region channel addressing a different audience IS counted. This is what
/// YouTube reports reliably: audience geography is not exposed for third-party channels and would have
/// to be estimated from indirect signals, producing a low-confidence, hard-to-audit metric. Changing
/// this partition is a change to this one function.
///
/// Decile rank is computed across the WHOLE RUN'S distinct language-matching channels, not against each
/// cluster's own (possibly single-channel) evidence in isolation — a channel is ranked once, however
/// many clusters mention it, so its competitive strength is relative to the run.
/// </summary>
public static class TargetMarketCompetition
{
    public static IReadOnlyList<double?> Compute(IReadOnlyList<TargetMarketComparisonInput> clusters, string language)
    {
        var decileByChannel = BuildDecileByChannel(clusters, language);
        var raw = clusters.Select(c => RawWeightedCompetition(c, language, decileByChannel)).ToList();
        return AvailabilityWeightedAggregate.NormalizeWithinSet(raw);
    }

    private static IReadOnlyDictionary<string, double> BuildDecileByChannel(
        IReadOnlyList<TargetMarketComparisonInput> clusters, string language)
    {
        var ranked = clusters
            .SelectMany(c => c.ClusterEvidence.Channels)
            .Where(c => string.Equals(c.LanguageCode, language, StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => c.ExternalRef, StringComparer.Ordinal)
            .Select(g => g.First())
            .Where(c => c.SubscriberCount.HasValue)
            .OrderBy(c => c.SubscriberCount!.Value)
            .ToList();

        var decileByChannel = new Dictionary<string, double>(StringComparer.Ordinal);

        for (var i = 0; i < ranked.Count; i++)
        {
            var decile = Math.Clamp((int)Math.Ceiling((i + 1) / (double)ranked.Count * 10), 1, 10);
            decileByChannel[ranked[i].ExternalRef] = decile / 10.0;
        }

        return decileByChannel;
    }

    private static double? RawWeightedCompetition(
        TargetMarketComparisonInput cluster, string language, IReadOnlyDictionary<string, double> decileByChannel)
    {
        var weights = cluster.ClusterEvidence.Channels
            .Where(c => string.Equals(c.LanguageCode, language, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ExternalRef)
            .Distinct(StringComparer.Ordinal)
            .Select(externalRef => decileByChannel.TryGetValue(externalRef, out var weight) ? weight : (double?)null)
            .Where(w => w.HasValue)
            .Select(w => w!.Value)
            .ToList();

        return weights.Count > 0 ? weights.Average() : null;
    }
}
