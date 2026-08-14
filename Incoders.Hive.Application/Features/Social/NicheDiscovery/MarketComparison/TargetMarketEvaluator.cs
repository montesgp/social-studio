using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Analytics;
using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.MarketComparison;

/// <summary>
/// One cluster's evidence and its already-computed metrics, ready for market comparison — the same
/// "call <c>Compute</c> again with a filtered <c>EvidenceSet</c>" per-cluster idiom
/// <see cref="DeterministicMetricCalculator"/> already establishes (design §6 class doc).
/// <see cref="ClusterMetrics"/> MUST be produced by <see cref="DeterministicMetricCalculator.Compute"/>
/// over the SAME <see cref="ClusterEvidence"/> — this evaluator never re-derives <c>VelocityProxy</c>.
/// </summary>
public sealed record TargetMarketComparisonInput(string ClusterKey, EvidenceSet ClusterEvidence, MetricSet ClusterMetrics);

/// <summary>
/// The four individually-inspectable values ND-08 requires (design §4, spec ND-08 scenario). Any value
/// may be <c>null</c> when the evidence cannot support the computation — never zero-filled (ND-05).
/// <see cref="TargetMarketGapValue"/> is <c>null</c> whenever demand or competition is <c>null</c>.
/// </summary>
public sealed record TargetMarketEvaluation(
    string ClusterKey,
    double? SourceMarketTraction,
    double? TargetMarketDemandValue,
    double? TargetMarketCompetitionValue,
    double? TargetMarketGapValue);

/// <summary>
/// Market comparison (ND-08, design §4). Pure, I/O-free: every input is already-normalized evidence
/// plus already-computed metrics, mirroring <see cref="Scoring.NicheOpportunityScorer"/>'s discipline.
///
/// No function here ever reads a market's IDENTITY, only its <see cref="MarketCode.Language"/> — which
/// is what makes a country-named branch structurally impossible (design §4). See
/// <see cref="TargetMarketCompetition"/>'s class doc for the accepted locale-vs-audience-geography
/// trade-off (owner decision, task 9.1).
/// </summary>
public static class TargetMarketEvaluator
{
    public static IReadOnlyList<TargetMarketEvaluation> Evaluate(
        IReadOnlyList<TargetMarketComparisonInput> clusters,
        MarketCode source,
        MarketCode target)
    {
        ArgumentNullException.ThrowIfNull(clusters);

        if (clusters.Count == 0)
        {
            return [];
        }

        var demand = TargetMarketDemand.Compute(clusters, target.Language);
        var competition = TargetMarketCompetition.Compute(clusters, target.Language);

        var results = new List<TargetMarketEvaluation>(clusters.Count);

        for (var i = 0; i < clusters.Count; i++)
        {
            var traction = TargetMarketDemand.RawMedianVelocity(clusters[i], source.Language);
            var gap = TargetMarketGap.Compute(demand[i], competition[i]);

            results.Add(new TargetMarketEvaluation(clusters[i].ClusterKey, traction, demand[i], competition[i], gap));
        }

        return results;
    }
}
