namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.niche_discovery_evidence</c> — append-only, navigable evidence backing
/// a <see cref="NicheOpportunityData"/> (design §9, ND-11). Metric columns are nullable: <c>null</c>
/// means the provider does not report the metric, <c>0</c> means it reported zero. Never zero-filled.
/// </summary>
public sealed class NicheDiscoveryEvidenceData
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    public Guid OpportunityId { get; set; }

    public Guid OrganizationId { get; set; }

    /// <summary>0 Channel / 1 Video (<c>NicheDiscoveryEvidenceKind</c>) — stored as a plain smallint so
    /// this entity never depends on the Application-layer enum type.</summary>
    public short EvidenceKind { get; set; }

    public string ExternalRef { get; set; } = string.Empty;

    public string ChannelHandle { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? ThumbnailUrl { get; set; }

    public DateTime? PublishedAt { get; set; }

    public long? Views { get; set; }

    public long? Likes { get; set; }

    public long? Comments { get; set; }

    public long? SubscriberCount { get; set; }

    public long? TotalViews { get; set; }

    public int? VideoCount { get; set; }

    public int? DurationSeconds { get; set; }

    public string? LanguageCode { get; set; }

    public string SourceKey { get; set; } = string.Empty;

    public DateTime ObservedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
