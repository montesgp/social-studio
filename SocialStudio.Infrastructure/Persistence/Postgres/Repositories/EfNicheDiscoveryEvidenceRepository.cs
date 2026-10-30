using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF implementation of <see cref="INicheDiscoveryEvidenceRepository"/> over
/// <c>social.niche_discovery_evidence</c> (design §9, ND-11). Append-only + navigable — no update or
/// delete path exists here either.
/// </summary>
public sealed class EfNicheDiscoveryEvidenceRepository : INicheDiscoveryEvidenceRepository
{
    private readonly AppIdentityDbContext _db;

    public EfNicheDiscoveryEvidenceRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(IReadOnlyList<NicheDiscoveryEvidenceRecord> evidence, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.Count == 0)
        {
            return;
        }

        var incomingKeys = evidence.Select(e => (e.RunId, e.OpportunityId, e.EvidenceKind, e.ExternalRef)).ToList();
        if (incomingKeys.Distinct().Count() != incomingKeys.Count)
        {
            throw new InvalidOperationException(
                "Duplicate (run_id, opportunity_id, evidence_kind, external_ref) inside one AppendAsync call — "
                + "a hard error under ux_niche_discovery_evidence_run_opportunity_kind_ref.");
        }

        var runIds = evidence.Select(e => e.RunId).Distinct().ToList();
        var existingKeys = await _db.NicheDiscoveryEvidence
            .Where(x => runIds.Contains(x.RunId))
            .Select(x => new { x.RunId, x.OpportunityId, x.EvidenceKind, x.ExternalRef })
            .ToListAsync(ct);
        var existingKeySet = existingKeys
            .Select(x => (x.RunId, x.OpportunityId, (NicheDiscoveryEvidenceKind)x.EvidenceKind, x.ExternalRef))
            .ToHashSet();

        if (incomingKeys.Any(existingKeySet.Contains))
        {
            throw new InvalidOperationException(
                "Duplicate (run_id, opportunity_id, evidence_kind, external_ref) against already-persisted evidence — "
                + "a hard error under ux_niche_discovery_evidence_run_opportunity_kind_ref.");
        }

        foreach (var item in evidence)
        {
            _db.NicheDiscoveryEvidence.Add(ToData(item));
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NicheDiscoveryEvidenceRecord>> ListByOpportunityAsync(
        Guid organizationId, Guid opportunityId, CancellationToken ct = default)
    {
        var rows = await _db.NicheDiscoveryEvidence
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.OpportunityId == opportunityId)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    private static NicheDiscoveryEvidenceData ToData(NicheDiscoveryEvidenceRecord r) => new()
    {
        Id = r.Id,
        RunId = r.RunId,
        OpportunityId = r.OpportunityId,
        OrganizationId = r.OrganizationId,
        EvidenceKind = (short)r.EvidenceKind,
        ExternalRef = r.ExternalRef,
        ChannelHandle = r.ChannelHandle,
        Title = r.Title,
        ThumbnailUrl = r.ThumbnailUrl,
        Description = r.Description,
        PublishedAt = r.PublishedAtUtc?.UtcDateTime,
        Views = r.Views,
        Likes = r.Likes,
        Comments = r.Comments,
        SubscriberCount = r.SubscriberCount,
        TotalViews = r.TotalViews,
        VideoCount = r.VideoCount,
        DurationSeconds = r.DurationSeconds,
        LanguageCode = r.LanguageCode,
        Country = r.Country,
        SourceKey = r.SourceKey,
        ObservedAt = r.ObservedAtUtc.UtcDateTime,
        CreatedAt = DateTime.UtcNow,
    };

    private static NicheDiscoveryEvidenceRecord ToDomain(NicheDiscoveryEvidenceData e) => new(
        Id: e.Id,
        RunId: e.RunId,
        OpportunityId: e.OpportunityId,
        OrganizationId: e.OrganizationId,
        EvidenceKind: (NicheDiscoveryEvidenceKind)e.EvidenceKind,
        ExternalRef: e.ExternalRef,
        ChannelHandle: e.ChannelHandle,
        Title: e.Title,
        ThumbnailUrl: e.ThumbnailUrl,
        Description: e.Description,
        PublishedAtUtc: e.PublishedAt is null ? null : new DateTimeOffset(e.PublishedAt.Value, TimeSpan.Zero),
        Views: e.Views,
        Likes: e.Likes,
        Comments: e.Comments,
        SubscriberCount: e.SubscriberCount,
        TotalViews: e.TotalViews,
        VideoCount: e.VideoCount,
        DurationSeconds: e.DurationSeconds,
        LanguageCode: e.LanguageCode,
        Country: e.Country,
        SourceKey: e.SourceKey,
        ObservedAtUtc: new DateTimeOffset(e.ObservedAt, TimeSpan.Zero));
}
