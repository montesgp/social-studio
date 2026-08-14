using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// Seeds the platform-level <c>social.market_catalog</c> table (design §4, §9, ND-14). Upsert-on-missing-
/// only, mirroring <see cref="NicheCatalogSeeder"/>: if a row already exists by code it is SKIPPED, so a
/// later activation/deactivation edit survives restarts. Writes through <see cref="AppIdentityDbContext"/>
/// directly rather than <c>IMarketCatalogRepository</c> — that port is deliberately READ-ONLY (design §9),
/// since no caller other than this seeder is ever meant to write the catalog.
/// </summary>
public sealed class MarketCatalogSeeder
{
    // Deterministic GUID. DO NOT change this value — later opportunities may value-cross-reference it.
    private static readonly SeedDefinition[] Catalog =
    [
        new(new Guid("00000000-0000-0000-0003-000000000001"), "es-AR", "social.markets.es_ar", "es"),
    ];

    private readonly AppIdentityDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<MarketCatalogSeeder> _logger;

    public MarketCatalogSeeder(AppIdentityDbContext db, IClock clock, ILogger<MarketCatalogSeeder> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var seed in Catalog)
        {
            var existing = await _db.MarketCatalog
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.MarketCode == seed.MarketCode, cancellationToken);

            if (existing is not null)
            {
                continue;
            }

            var codeResult = MarketCode.TryParse(seed.MarketCode);
            if (codeResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to seed market catalog entry '{MarketCode}': {Error}", seed.MarketCode, codeResult.Error.Code);
                throw new InvalidOperationException(
                    $"Failed to seed market catalog entry '{seed.MarketCode}': {codeResult.Error.Code}");
            }

            var entry = MarketCatalogEntry.Restore(
                new MarketId(seed.Id), codeResult.Value, seed.DisplayNameKey, seed.PrimaryLanguage, isActive: true);

            _db.MarketCatalog.Add(new MarketCatalogData
            {
                Id = entry.Id.Value,
                MarketCode = entry.Code.Value,
                DisplayNameKey = entry.DisplayNameKey,
                PrimaryLanguage = entry.PrimaryLanguage,
                IsActive = entry.IsActive,
                CreatedAt = _clock.UtcNow,
            });

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Seeded market catalog entry '{MarketCode}' ({Id})", seed.MarketCode, seed.Id);
        }
    }

    private sealed record SeedDefinition(Guid Id, string MarketCode, string DisplayNameKey, string PrimaryLanguage);
}
