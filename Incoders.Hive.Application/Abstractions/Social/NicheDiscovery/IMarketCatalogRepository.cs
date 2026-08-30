using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// Read-only port over the seeded <c>social.market_catalog</c> table (design §9, ND-14). Adding a
/// second market is a seeder row; this port needs no change for that to work.
/// </summary>
public interface IMarketCatalogRepository
{
    Task<IReadOnlyList<MarketCatalogEntry>> ListActiveAsync(CancellationToken ct = default);

    Task<MarketCatalogEntry?> GetByCodeAsync(MarketCode code, CancellationToken ct = default);
}
