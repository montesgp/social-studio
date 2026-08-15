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
    string? FailureReasonKey)
{
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
        run.FailureReasonKey);
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
    int Rank)
{
    public static NicheOpportunitySummary FromRecord(NicheOpportunityRecord record, IReadOnlyList<ExcludedSignal> excludedSignals) => new(
        record.Id,
        record.SubNicheLabel,
        record.ContentPattern,
        record.TopicLabels,
        record.OpportunityScore,
        record.ConfidenceScore,
        record.EffectiveWeightDenominator,
        excludedSignals,
        record.Rank);
}

public sealed record RunOpportunitiesResponse(IReadOnlyList<NicheOpportunitySummary> Opportunities);

public sealed record OpportunityDetailResponse(
    NicheOpportunityRecord Opportunity,
    IReadOnlyList<ExcludedSignal> ExcludedSignals,
    IReadOnlyList<NicheDiscoveryEvidenceRecord> Evidence);
