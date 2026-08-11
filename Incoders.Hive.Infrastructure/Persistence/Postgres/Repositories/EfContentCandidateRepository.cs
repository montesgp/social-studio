using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF implementation of <see cref="IContentCandidateRepository"/> over
/// <c>social.discovered_candidates</c> (design §5.1, D6, CD-08).
/// </summary>
public sealed class EfContentCandidateRepository : IContentCandidateRepository
{
    private readonly AppIdentityDbContext _db;

    public EfContentCandidateRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task RecordObservationsAsync(
        Guid organizationId,
        Guid clientId,
        IReadOnlyList<CandidateObservation> observations,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (observations.Count == 0)
        {
            return;
        }

        // CD-08: a double-write of the SAME candidate at the SAME observed_at inside ONE call is
        // rejected — not silently duplicated. Mirrors the unique index (organization_id, client_id,
        // candidate_key, observed_at) that also protects this at the database layer in production.
        var deduped = observations
            .GroupBy(o => (o.CandidateKey, o.ObservedAtUtc))
            .Select(g => g.First());

        foreach (var observation in deduped)
        {
            _db.DiscoveredCandidates.Add(Map(organizationId, clientId, observation));
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CandidateObservation>> GetLatestByKeysAsync(
        Guid organizationId,
        Guid clientId,
        IReadOnlyList<string> candidateKeys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidateKeys);

        if (candidateKeys.Count == 0)
        {
            return [];
        }

        var rows = await _db.DiscoveredCandidates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                        && x.ClientId == clientId
                        && candidateKeys.Contains(x.CandidateKey))
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.CandidateKey)
            .Select(g => g.OrderByDescending(x => x.ObservedAt).First())
            .Select(Map)
            .ToList();
    }

    private static DiscoveredCandidateData Map(Guid organizationId, Guid clientId, CandidateObservation o) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        ClientId = clientId,
        CandidateKey = o.CandidateKey,
        NetworkKey = o.NetworkKey,
        SourceKey = o.SourceKey,
        SearchCriterion = o.SearchCriterion,
        ContentUrl = o.ContentUrl,
        AuthorHandle = o.AuthorHandle,
        Title = o.Title,
        Summary = o.Summary,
        ThumbnailUrl = o.ThumbnailUrl,
        PublishedAt = o.PublishedAtUtc?.UtcDateTime,
        Views = o.Views,
        Likes = o.Likes,
        Comments = o.Comments,
        Shares = o.Shares,
        Saves = o.Saves,
        OpportunityScore = o.OpportunityScore,
        Classification = o.Classification,
        ObservedAt = o.ObservedAtUtc.UtcDateTime,
        CreatedAt = DateTime.UtcNow,
    };

    private static CandidateObservation Map(DiscoveredCandidateData e) => new(
        CandidateKey: e.CandidateKey,
        NetworkKey: e.NetworkKey,
        SourceKey: e.SourceKey,
        SearchCriterion: e.SearchCriterion,
        ContentUrl: e.ContentUrl,
        AuthorHandle: e.AuthorHandle,
        Title: e.Title,
        Summary: e.Summary,
        ThumbnailUrl: e.ThumbnailUrl,
        PublishedAtUtc: e.PublishedAt is null ? null : new DateTimeOffset(e.PublishedAt.Value, TimeSpan.Zero),
        Views: e.Views,
        Likes: e.Likes,
        Comments: e.Comments,
        Shares: e.Shares,
        Saves: e.Saves,
        OpportunityScore: e.OpportunityScore,
        Classification: e.Classification,
        ObservedAtUtc: new DateTimeOffset(e.ObservedAt, TimeSpan.Zero));
}
