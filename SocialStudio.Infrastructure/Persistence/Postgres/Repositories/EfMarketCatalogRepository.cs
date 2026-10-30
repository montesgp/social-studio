using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Domain.Social.NicheDiscovery;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>
/// EF implementation of the deliberately READ-ONLY <see cref="IMarketCatalogRepository"/> over the
/// platform-level <c>social.market_catalog</c> table (design §9, ND-14). Writes are the seeder's job
/// (<c>MarketCatalogSeeder</c>), not this port's.
/// </summary>
public sealed class EfMarketCatalogRepository : IMarketCatalogRepository
{
    private readonly AppIdentityDbContext _db;

    public EfMarketCatalogRepository(AppIdentityDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MarketCatalogEntry>> ListActiveAsync(CancellationToken ct = default)
    {
        var rows = await _db.MarketCatalog
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<MarketCatalogEntry?> GetByCodeAsync(MarketCode code, CancellationToken ct = default)
    {
        var row = await _db.MarketCatalog
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.MarketCode == code.Value, ct);

        return row is null ? null : ToDomain(row);
    }

    private static MarketCatalogEntry ToDomain(MarketCatalogData row)
    {
        var code = MarketCode.TryParse(row.MarketCode);

        // Only the seeder can ever write this table, and it always writes a value that already
        // passed MarketCode.TryParse — a failure here means the stored row was corrupted outside
        // the seeder, a hard invariant violation, not a normal "not found" path.
        if (code.IsFailure)
        {
            throw new InvalidOperationException(
                $"Persisted market catalog entry '{row.MarketCode}' has a corrupted code. Id={row.Id}");
        }

        return MarketCatalogEntry.Restore(
            new MarketId(row.Id), code.Value, row.DisplayNameKey, row.PrimaryLanguage, row.IsActive);
    }
}
