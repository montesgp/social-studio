using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF implementation of <see cref="IAccountBaselineRepository"/> over
/// <c>social.account_baselines</c> (design §5.2, CD-09). No <c>client_id</c> anywhere — a baseline is
/// a property of the author, not of the client that happened to discover them.
/// </summary>
public sealed class EfAccountBaselineRepository : IAccountBaselineRepository
{
    private readonly AppIdentityDbContext _db;

    public EfAccountBaselineRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task<AccountBaseline?> GetAsync(
        Guid organizationId,
        string networkKey,
        string authorHandle,
        CancellationToken ct = default)
    {
        var normalizedHandle = Normalize(authorHandle);

        var entity = await _db.AccountBaselines
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId
                     && x.NetworkKey == networkKey
                     && x.AuthorHandle == normalizedHandle,
                ct);

        return entity is null ? null : Map(entity);
    }

    public async Task UpsertAsync(
        Guid organizationId,
        AccountBaseline baseline,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        var normalizedHandle = Normalize(baseline.AuthorHandle);

        // The unique key is (organization_id, network_key, lower(author_handle)) — normalizing the
        // handle here, once, is what lets a plain (not functional) unique index on the column do the
        // same job the design's lower() expression describes (§5.2).
        var existing = await _db.AccountBaselines
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId
                     && x.NetworkKey == baseline.NetworkKey
                     && x.AuthorHandle == normalizedHandle,
                ct);

        if (existing is null)
        {
            _db.AccountBaselines.Add(new AccountBaselineData
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                NetworkKey = baseline.NetworkKey,
                AuthorHandle = normalizedHandle,
                MedianViews = baseline.MedianViews,
                MedianEngagement = baseline.MedianEngagement,
                PostCount = baseline.PostCount,
                ComputedAt = baseline.ComputedAt.UtcDateTime,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.MedianViews = baseline.MedianViews;
            existing.MedianEngagement = baseline.MedianEngagement;
            existing.PostCount = baseline.PostCount;
            existing.ComputedAt = baseline.ComputedAt.UtcDateTime;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string Normalize(string handle) => handle.Trim().ToLowerInvariant();

    private static AccountBaseline Map(AccountBaselineData e) => new(
        NetworkKey: e.NetworkKey,
        AuthorHandle: e.AuthorHandle,
        MedianViews: e.MedianViews,
        MedianEngagement: e.MedianEngagement,
        PostCount: e.PostCount,
        ComputedAt: new DateTimeOffset(e.ComputedAt, TimeSpan.Zero));
}
