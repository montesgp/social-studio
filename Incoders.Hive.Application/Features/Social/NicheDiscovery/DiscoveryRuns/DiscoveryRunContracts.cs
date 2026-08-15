using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.Scoring;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>Response DTOs shared by the four NicheDiscovery run/opportunity handlers (design §10).</summary>
public sealed record StartDiscoveryRunResponse(Guid RunId, string Status);

public sealed record DiscoveryRunStatusResponse(
    Guid RunId,
    string Status,
    int WindowDays,
    string Depth,
    int ProviderUnitsSpent,
    int AiCompletionsSpent,
    bool SemanticDegraded,
    IReadOnlyList<string> UnconfiguredSourceKeys,
    IReadOnlyList<string> FailedSourceKeys,
    string? FailureReasonKey,
    string TargetMarketCode,
    string ContentFormat)
{
    // WU-12 gap fix: the viability dashboard (ND-12) filters/sorts by market and format, and every
    // opportunity produced by a run shares that run's target market and content format (they are
    // run-level parameters, not per-opportunity — design §2.1). Neither was on this projection
    // before WU-12 needed it to label and filter opportunities client-side.
    public static DiscoveryRunStatusResponse FromRun(Domain.Social.NicheDiscovery.DiscoveryRun run) => new(
        run.Id.Value,
        run.Status.ToString(),
        run.Window.Days,
        run.Depth.ToString(),
        run.ProviderUnitsSpent,
        run.AiCompletionsSpent,
        run.SemanticDegraded,
        run.UnconfiguredSourceKeys,
        run.FailedSourceKeys,
        run.FailureReasonKey,
        run.TargetMarketCode.Value,
        run.ContentFormat);
}

public sealed record NicheOpportunitySummary(
    Guid Id,
    string? SubNicheLabel,
    string ContentPattern,
    IReadOnlyList<string> TopicLabels,
    double OpportunityScore,
    double ConfidenceScore,
    double EffectiveWeightDenominator,
    IReadOnlyList<ExcludedSignal> ExcludedSignals,
    int Rank,
    double? TargetMarketCompetition,
    double? AutomationSuitability,
    double? GrowthProxy)
{
    // WU-12 gap fix: the dashboard list (ND-12) sorts/filters by target-market competition,
    // automation suitability, and trend directly from this projection, without a second round trip
    // to the detail endpoint for every row.
    public static NicheOpportunitySummary FromRecord(NicheOpportunityRecord record, IReadOnlyList<ExcludedSignal> excludedSignals) => new(
        record.Id,
        record.SubNicheLabel,
        record.ContentPattern,
        record.TopicLabels,
        record.OpportunityScore,
        record.ConfidenceScore,
        record.EffectiveWeightDenominator,
        excludedSignals,
        record.Rank,
        record.TargetMarketCompetition,
        record.AutomationSuitability,
        record.GrowthProxy);
}

public sealed record RunOpportunitiesResponse(IReadOnlyList<NicheOpportunitySummary> Opportunities);

public sealed record OpportunityDetailResponse(
    NicheOpportunityRecord Opportunity,
    IReadOnlyList<ExcludedSignal> ExcludedSignals,
    IReadOnlyList<NicheDiscoveryEvidenceRecord> Evidence);
