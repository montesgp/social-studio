namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.discovered_candidates</c> — append-only observation rows sharing a
/// stable <c>candidate_key</c> (design §5.1, D6). One row per (org, client, candidate_key,
/// observed_at); the same content observed again in a later run gets a NEW row, never an update, so
/// history is never lost.
///
/// Metric columns are nullable bigints (D2, CD-04): null means the network does not report the
/// metric, 0 means it reported zero. Never zero-filled.
/// </summary>
public sealed class DiscoveredCandidateData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClientId { get; set; }

    /// <summary>The stable dedup identity (<c>CandidateIdentity.From</c>) — shared by the chain's
    /// in-run dedup and cross-run persisted identity so the two can never disagree (D3).</summary>
    public string CandidateKey { get; set; } = string.Empty;

    public string NetworkKey { get; set; } = string.Empty;

    public string SourceKey { get; set; } = string.Empty;

    public string SearchCriterion { get; set; } = string.Empty;

    public string ContentUrl { get; set; } = string.Empty;

    public string AuthorHandle { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string? ThumbnailUrl { get; set; }

    /// <summary>Null when the source could not resolve a publish date — classification then MUST be
    /// Undetermined (CD-06).</summary>
    public DateTime? PublishedAt { get; set; }

    public long? Views { get; set; }

    public long? Likes { get; set; }

    public long? Comments { get; set; }

    public long? Shares { get; set; }

    public long? Saves { get; set; }

    /// <summary>Opportunity score AT OBSERVATION TIME — a later re-score of the same candidate lands
    /// in a new row, never rewrites this one.</summary>
    public double? OpportunityScore { get; set; }

    /// <summary>0 Undetermined / 1 Trending / 2 Proven (<c>ContentClassification</c>) — stored as a
    /// plain smallint so this entity never depends on the Application-layer enum type.</summary>
    public short? Classification { get; set; }

    public DateTime ObservedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
