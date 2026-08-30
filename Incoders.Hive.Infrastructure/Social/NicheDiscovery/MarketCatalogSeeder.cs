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
    /// <summary>
    /// ISO 3166 "unspecified region". Discovery is partitioned by LANGUAGE — no algorithm reads a
    /// market's region (see <c>MarketCode.Language</c> and <c>TargetMarketEvaluator</c>) — so seeding a
    /// representative country per language would advertise a partition the engine does not implement:
    /// two rows differing only in region produce byte-identical runs. <c>ZZ</c> states the absence
    /// honestly instead. Region-aware markets are a separate change to the sourcing query and the
    /// competition partition, not a catalog edit.
    /// </summary>
    private const string UnspecifiedRegion = "ZZ";

    // Deterministic GUIDs. DO NOT change these values — later opportunities may value-cross-reference
    // them. Each new market takes the next id in the 0003 block; ids 001/002 are retired (see
    // SupersededCodes) and MUST NOT be reused.
    //
    // The languages carrying the most YouTube content and audience worldwide. Ordered by that weight,
    // not alphabetically, because the selector renders them in catalog order.
    private static readonly SeedDefinition[] Catalog =
    [
        new(new Guid("00000000-0000-0000-0003-000000000010"), "en", "social.markets.en"),
        new(new Guid("00000000-0000-0000-0003-000000000011"), "es", "social.markets.es"),
        new(new Guid("00000000-0000-0000-0003-000000000012"), "hi", "social.markets.hi"),
        new(new Guid("00000000-0000-0000-0003-000000000013"), "pt", "social.markets.pt"),
        new(new Guid("00000000-0000-0000-0003-000000000014"), "id", "social.markets.id"),
        new(new Guid("00000000-0000-0000-0003-000000000015"), "ar", "social.markets.ar"),
        new(new Guid("00000000-0000-0000-0003-000000000016"), "ru", "social.markets.ru"),
        new(new Guid("00000000-0000-0000-0003-000000000017"), "ja", "social.markets.ja"),
        new(new Guid("00000000-0000-0000-0003-000000000018"), "ko", "social.markets.ko"),
        new(new Guid("00000000-0000-0000-0003-000000000019"), "fr", "social.markets.fr"),
        new(new Guid("00000000-0000-0000-0003-00000000001a"), "de", "social.markets.de"),
        new(new Guid("00000000-0000-0000-0003-00000000001b"), "tr", "social.markets.tr"),
        new(new Guid("00000000-0000-0000-0003-00000000001c"), "vi", "social.markets.vi"),
        new(new Guid("00000000-0000-0000-0003-00000000001d"), "th", "social.markets.th"),
        new(new Guid("00000000-0000-0000-0003-00000000001e"), "it", "social.markets.it"),
        new(new Guid("00000000-0000-0000-0003-00000000001f"), "pl", "social.markets.pl"),
        new(new Guid("00000000-0000-0000-0003-000000000020"), "bn", "social.markets.bn"),
    ];

    /// <summary>
    /// Region-specific rows this catalog replaced. They are DEACTIVATED rather than deleted: a run
    /// already persisted against one of these codes must keep resolving, but the operator must stop
    /// being offered two rows that produce identical results. Deactivation is idempotent.
    /// </summary>
    private static readonly string[] SupersededCodes = ["es-AR", "pt-BR"];

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
            var marketCode = $"{seed.Language}-{UnspecifiedRegion}";

            var existing = await _db.MarketCatalog
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.MarketCode == marketCode, cancellationToken);

            if (existing is not null)
            {
                continue;
            }

            var codeResult = MarketCode.TryParse(marketCode);
            if (codeResult.IsFailure)
            {
                _logger.LogError(
                    "Failed to seed market catalog entry '{MarketCode}': {Error}", marketCode, codeResult.Error.Code);
                throw new InvalidOperationException(
                    $"Failed to seed market catalog entry '{marketCode}': {codeResult.Error.Code}");
            }

            var entry = MarketCatalogEntry.Restore(
                new MarketId(seed.Id), codeResult.Value, seed.DisplayNameKey, seed.Language, isActive: true);

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

            _logger.LogInformation("Seeded market catalog entry '{MarketCode}' ({Id})", marketCode, seed.Id);
        }

        await DeactivateSupersededAsync(cancellationToken);
    }

    private async Task DeactivateSupersededAsync(CancellationToken cancellationToken)
    {
        var superseded = await _db.MarketCatalog
            .Where(x => SupersededCodes.Contains(x.MarketCode) && x.IsActive)
            .ToListAsync(cancellationToken);

        if (superseded.Count == 0)
        {
            return;
        }

        foreach (var row in superseded)
        {
            row.IsActive = false;
            _logger.LogInformation(
                "Deactivated superseded region-specific market '{MarketCode}' — discovery partitions by language",
                row.MarketCode);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>One seeded market: a language, with its region left unspecified.</summary>
    private sealed record SeedDefinition(Guid Id, string Language, string DisplayNameKey);
}
