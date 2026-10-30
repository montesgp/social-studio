namespace SocialStudio.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// One persisted, append-only observation of a <c>NicheOpportunity</c> (design §9). <c>NicheId</c> is a
/// deliberate value cross-reference, never an FK — the catalogs are parallel (N5/settled decision 2).
/// </summary>
public sealed record NicheOpportunityRecord(
    Guid Id,
    Guid RunId,
    Guid OrganizationId,
    Guid? NicheId,
    string? SubNicheLabel,
    string ContentPattern,
    IReadOnlyList<string> TopicLabels,
    double OpportunityScore,
    double ConfidenceScore,
    double EffectiveWeightDenominator,
    string ExcludedSignalsJson,
    long? MedianViews,
    double? OutlierRatio,
    double? OutlierDensity,
    double? GrowthProxy,
    int? CrossChannelRepetition,
    double? TargetMarketDemand,
    double? TargetMarketCompetition,
    double? TargetMarketGap,
    /// <summary>
    /// Target-market demand before run-normalization. <see cref="TargetMarketDemand"/> ranks within
    /// the run; only this value carries absolute magnitude, so it is what an absolute-viability rule
    /// must read. <c>null</c> when the target market has no measurable video — never zero-filled.
    /// </summary>
    double? TargetMarketRawDemand,
    double? AutomationSuitability,
    int ChannelSampleSize,
    int VideoSampleSize,
    int Rank,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Port over the append-only <c>social.niche_opportunities</c> read model (design §9, ND-11).
/// Append-only is enforced HERE: no update or delete method exists on this port. Implemented by
/// <c>EfNicheOpportunityRepository</c> in Infrastructure (WU-7b).
/// </summary>
public interface INicheOpportunityRepository
{
    Task AppendAsync(IReadOnlyList<NicheOpportunityRecord> opportunities, CancellationToken ct = default);

    Task<IReadOnlyList<NicheOpportunityRecord>> ListByRunAsync(
        Guid organizationId, Guid runId, CancellationToken ct = default);

    Task<NicheOpportunityRecord?> GetByIdAsync(
        Guid organizationId, Guid opportunityId, CancellationToken ct = default);
}
