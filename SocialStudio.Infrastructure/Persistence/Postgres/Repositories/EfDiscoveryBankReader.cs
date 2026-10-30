using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF implementation of <see cref="IDiscoveryBankReader"/> over <c>social.discovered_candidates</c>.
///
/// Read-only by construction: it holds no <c>SaveChangesAsync</c> and every query is
/// <see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}"/>. The AI Analysis tool must not
/// be able to mutate the bank even by accident.
///
/// <c>discovered_candidates</c> is APPEND-ONLY: one candidate observed across several runs has one row
/// per observation. The tool reports on candidates, not observations, so the newest observation of each
/// candidate key wins — otherwise a candidate that discovery happened to re-see three times would
/// outrank a stronger one simply by appearing three times in a count.
/// </summary>
public sealed class EfDiscoveryBankReader : IDiscoveryBankReader
{
    private readonly AppIdentityDbContext _db;

    public EfDiscoveryBankReader(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DiscoveryBankRow>> ListAsync(
        Guid organizationId,
        CancellationToken ct = default)
    {
        // The SQL filter narrows to ONE organization; the newest-per-candidate collapse then happens in
        // memory. A grouped "latest row per key" projection has no LINQ translation shared by the
        // Postgres provider used in production and the in-memory provider the tests run under, and a
        // provider-specific expression here would silently change behaviour between the two — the same
        // reasoning EfContentCandidateRepository.GetNicheBankAsync already records.
        var rows = await _db.DiscoveredCandidates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.CandidateKey, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(x => x.ObservedAt)
                .ThenBy(x => x.Id)
                .First())
            .OrderByDescending(x => x.ObservedAt)
            .ThenBy(x => x.CandidateKey, StringComparer.Ordinal)
            .Select(Map)
            .ToList();
    }

    private static DiscoveryBankRow Map(DiscoveredCandidateData e) => new(
        CandidateKey: e.CandidateKey,
        NetworkKey: e.NetworkKey,
        // Only bank rows carry hashtags; a client-scoped observation was found by profile mining and has
        // none. An empty list is the honest answer — never a placeholder tag that a filter could match.
        Hashtags: e.BankHashtags is null ? [] : e.BankHashtags.ToList(),
        ContentUrl: e.ContentUrl,
        AuthorHandle: e.AuthorHandle,
        Title: e.Title,
        PublishedAtUtc: e.PublishedAt is null ? null : new DateTimeOffset(e.PublishedAt.Value, TimeSpan.Zero),
        ObservedAtUtc: new DateTimeOffset(e.ObservedAt, TimeSpan.Zero),
        Views: e.Views,
        Likes: e.Likes,
        Comments: e.Comments,
        Shares: e.Shares,
        Saves: e.Saves,
        OpportunityScore: e.OpportunityScore);
}
