namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

public enum NicheDiscoveryEvidenceKind
{
    Channel = 0,
    Video = 1,
}

/// <summary>One persisted, append-only evidence row backing a <c>NicheOpportunity</c> (design §9, ND-11).</summary>
public sealed record NicheDiscoveryEvidenceRecord(
    Guid Id,
    Guid RunId,
    Guid OpportunityId,
    Guid OrganizationId,
    NicheDiscoveryEvidenceKind EvidenceKind,
    string ExternalRef,
    string ChannelHandle,
    string Title,
    string? ThumbnailUrl,
    /// <summary>Provider-reported description of the channel behind this row. Review context only.</summary>
    string? Description,
    DateTimeOffset? PublishedAtUtc,
    long? Views,
    long? Likes,
    long? Comments,
    long? SubscriberCount,
    long? TotalViews,
    int? VideoCount,
    int? DurationSeconds,
    string? LanguageCode,
    /// <summary>
    /// Provider-reported ISO 3166 code of the channel behind this row — opaque, never branched on
    /// (design §3). Discovery partitions by language, so this is REVIEW CONTEXT for the operator, not
    /// an input to any metric. <c>null</c> when the provider does not report it: never inferred from
    /// the run's market.
    /// </summary>
    string? Country,
    string SourceKey,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Port over the append-only, navigable <c>social.niche_discovery_evidence</c> table (design §9,
/// ND-11). Append-only is enforced HERE: no update or delete method exists on this port. Implemented
/// by <c>EfNicheDiscoveryEvidenceRepository</c> in Infrastructure (WU-7b).
/// </summary>
public interface INicheDiscoveryEvidenceRepository
{
    Task AppendAsync(IReadOnlyList<NicheDiscoveryEvidenceRecord> evidence, CancellationToken ct = default);

    Task<IReadOnlyList<NicheDiscoveryEvidenceRecord>> ListByOpportunityAsync(
        Guid organizationId, Guid opportunityId, CancellationToken ct = default);
}
