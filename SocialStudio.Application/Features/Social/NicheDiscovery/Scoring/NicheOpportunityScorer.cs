namespace SocialStudio.Application.Features.Social.NicheDiscovery.Scoring;

/// <summary>
/// Pure opportunity scorer (ND-09, design §3 layer 4, §7.1). No I/O, no EF, no provider types — only
/// plain records and a caller-supplied <c>nowUtc</c>, mirroring
/// <c>Social.ContentGeneration.Discovery.OpportunityScorer</c>'s discipline. <c>CrossPlatformValidation</c>
/// has no data source in this change and is PERMANENTLY excluded (<c>NotSupportedYet</c>, N8) —
/// renormalized out of the denominator and surfaced in <see cref="ScoredOpportunity.ExcludedSignals"/>,
/// never silently dropped and never treated as a zero score.
/// </summary>
public static class NicheOpportunityScorer
{
    public const string CrossPlatformValidationSignalKey = "cross_platform_validation";

    private const string NotSupportedYetReasonKey = "not_supported_yet";
    private const string NotReportedReasonKey = "not_reported";
    private const string NotSupportedYetI18nKey = "social.niche_discovery.signals.not_supported_yet";
    private const string I18nKeyPrefix = "social.niche_discovery.signals.";

    public static IReadOnlyList<ScoredOpportunity> Score(
        IReadOnlyList<OpportunityInput> set,
        OpportunityScoreWeights weights,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(weights);
        // Reserved for future time-dependent signals (mirrors OpportunityScorer's "never
        // DateTimeOffset.UtcNow internally" discipline) — every current WU-8 signal is a precomputed,
        // already set-relative-normalizable value, so nowUtc is unused today.
        _ = nowUtc;

        if (set.Count == 0)
        {
            return [];
        }

        var viral = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(i => i.ViralPerformance).ToList());
        var crossChannel = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(i => i.CrossChannelValidation).ToList());
        var demand = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(i => i.DemandMomentum).ToList());
        var gap = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(i => i.TargetMarketGap).ToList());
        var automation = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(i => i.AutomationSuitability).ToList());
        var evergreen = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(i => i.EvergreenPotential).ToList());
        var monetization = AvailabilityWeightedAggregate.NormalizeWithinSet(set.Select(i => i.MonetizationPotential).ToList());

        var results = new List<ScoredOpportunity>(set.Count);

        for (var i = 0; i < set.Count; i++)
        {
            var signals = new (string Key, double Weight, double? Value, string ReasonKey)[]
            {
                ("viral_performance", weights.ViralPerformance, viral[i], NotReportedReasonKey),
                ("cross_channel_validation", weights.CrossChannelValidation, crossChannel[i], NotReportedReasonKey),
                ("demand_momentum", weights.DemandMomentum, demand[i], NotReportedReasonKey),
                ("target_market_gap", weights.TargetMarketGap, gap[i], NotReportedReasonKey),
                ("automation_suitability", weights.AutomationSuitability, automation[i], NotReportedReasonKey),
                ("evergreen_potential", weights.EvergreenPotential, evergreen[i], NotReportedReasonKey),
                (CrossPlatformValidationSignalKey, weights.CrossPlatformValidation, null, NotSupportedYetReasonKey),
                ("monetization_potential", weights.MonetizationPotential, monetization[i], NotReportedReasonKey),
            };

            var weighted = signals.Select(s => (s.Weight, s.Value)).ToList();
            var score = AvailabilityWeightedAggregate.Score(weighted);
            var effectiveDenominator = AvailabilityWeightedAggregate.EffectiveWeightDenominator(weighted);

            var excluded = signals
                .Where(s => s.Value is null)
                .Select(s => new ExcludedSignal(
                    s.Key,
                    s.ReasonKey,
                    s.Key == CrossPlatformValidationSignalKey ? NotSupportedYetI18nKey : I18nKeyPrefix + s.ReasonKey))
                .ToList();

            results.Add(new ScoredOpportunity(set[i].ExternalRef, score, effectiveDenominator, excluded));
        }

        // Total order: highest score first, ties broken ordinally on identity so ranking never depends
        // on input order — mirrors OpportunityScorer.Score.
        return results
            .OrderByDescending(r => r.OpportunityScore)
            .ThenBy(r => r.ExternalRef, StringComparer.Ordinal)
            .ToList();
    }
}
