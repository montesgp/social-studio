using Incoders.Hive.Domain.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Social.NicheDiscovery;
using Incoders.Hive.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// ND-14. The catalog seeds one row PER LANGUAGE with the region left unspecified (<c>ZZ</c>), because
/// no algorithm reads a market's region — two rows differing only in region would produce identical
/// runs. Seeding remains upsert-on-missing-only, mirroring <c>NicheCatalogSeederTests</c>, so a later
/// activation edit survives restarts.
/// </summary>
public sealed class MarketCatalogSeederTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SeedAsync_SeedsEveryMarketWithAnUnspecifiedRegion()
    {
        await using var db = CreateDb();

        await Build(db).SeedAsync(CancellationToken.None);

        var codes = await db.MarketCatalog.Select(x => x.MarketCode).ToListAsync();
        Assert.NotEmpty(codes);
        Assert.All(codes, code => Assert.EndsWith("-ZZ", code, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeedAsync_SeedsEveryCodeInAFormTheDomainAccepts()
    {
        await using var db = CreateDb();

        await Build(db).SeedAsync(CancellationToken.None);

        var codes = await db.MarketCatalog.Select(x => x.MarketCode).ToListAsync();

        Assert.All(codes, code => Assert.True(
            MarketCode.TryParse(code).IsSuccess,
            $"Seeded market code '{code}' is not parseable by the domain value object."));
    }

    /// <summary>
    /// The language IS the partition — <c>MarketCode.Language</c> is the only part any algorithm reads
    /// — so a seeded row whose PrimaryLanguage disagreed with its code would silently discover in one
    /// language while claiming another.
    /// </summary>
    [Fact]
    public async Task SeedAsync_KeepsPrimaryLanguageConsistentWithTheMarketCode()
    {
        await using var db = CreateDb();

        await Build(db).SeedAsync(CancellationToken.None);

        var rows = await db.MarketCatalog.ToListAsync();

        Assert.All(rows, row => Assert.Equal(MarketCode.TryParse(row.MarketCode).Value.Language, row.PrimaryLanguage));
    }

    [Theory]
    [InlineData("en-ZZ")]
    [InlineData("es-ZZ")]
    [InlineData("hi-ZZ")]
    [InlineData("pt-ZZ")]
    [InlineData("id-ZZ")]
    [InlineData("pl-ZZ")]
    public async Task SeedAsync_SeedsTheHighVolumeYouTubeLanguages(string marketCode)
    {
        await using var db = CreateDb();

        await Build(db).SeedAsync(CancellationToken.None);

        var seeded = await db.MarketCatalog.SingleOrDefaultAsync(x => x.MarketCode == marketCode);
        Assert.NotNull(seeded);
        Assert.True(seeded!.IsActive);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_DoesNotDuplicateAnyRow()
    {
        await using var db = CreateDb();
        var seeder = Build(db);

        await seeder.SeedAsync(CancellationToken.None);
        var afterFirst = await db.MarketCatalog.CountAsync();

        await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(afterFirst, await db.MarketCatalog.CountAsync());
    }

    [Fact]
    public async Task SeedAsync_GivesEverySeededMarketItsOwnDeterministicId()
    {
        await using var firstDb = CreateDb();
        await Build(firstDb).SeedAsync(CancellationToken.None);

        await using var secondDb = CreateDb();
        await Build(secondDb).SeedAsync(CancellationToken.None);

        var first = await firstDb.MarketCatalog.OrderBy(x => x.MarketCode).Select(x => new { x.MarketCode, x.Id }).ToListAsync();
        var second = await secondDb.MarketCatalog.OrderBy(x => x.MarketCode).Select(x => new { x.MarketCode, x.Id }).ToListAsync();

        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Select(x => x.Id).Distinct().Count());
    }

    /// <summary>
    /// The start-run form allows source == target, but a one-row catalog would still make a
    /// cross-language study impossible.
    /// </summary>
    [Fact]
    public async Task SeedAsync_SeedsEnoughLanguagesForACrossLanguageStudy()
    {
        await using var db = CreateDb();

        await Build(db).SeedAsync(CancellationToken.None);

        Assert.True(await db.MarketCatalog.CountAsync(x => x.IsActive) >= 2);
    }

    // ── superseded region-specific rows ──────────────────────────────────────

    [Fact]
    public async Task SeedAsync_DeactivatesSupersededRegionSpecificRows_WithoutDeletingThem()
    {
        await using var db = CreateDb();
        db.MarketCatalog.Add(LegacyRow("es-AR", "es"));
        await db.SaveChangesAsync();

        await Build(db).SeedAsync(CancellationToken.None);

        var legacy = await db.MarketCatalog.SingleAsync(x => x.MarketCode == "es-AR");
        Assert.False(legacy.IsActive);
    }

    [Fact]
    public async Task SeedAsync_DeactivationIsIdempotent()
    {
        await using var db = CreateDb();
        db.MarketCatalog.Add(LegacyRow("pt-BR", "pt"));
        await db.SaveChangesAsync();
        var seeder = Build(db);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        Assert.Equal(1, await db.MarketCatalog.CountAsync(x => x.MarketCode == "pt-BR"));
        Assert.False((await db.MarketCatalog.SingleAsync(x => x.MarketCode == "pt-BR")).IsActive);
    }

    [Fact]
    public async Task SeedAsync_LeavesTheLanguageRowsActive_WhenDeactivatingSupersededOnes()
    {
        await using var db = CreateDb();
        db.MarketCatalog.Add(LegacyRow("es-AR", "es"));
        await db.SaveChangesAsync();

        await Build(db).SeedAsync(CancellationToken.None);

        var languageRow = await db.MarketCatalog.SingleAsync(x => x.MarketCode == "es-ZZ");
        Assert.True(languageRow.IsActive);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static Incoders.Hive.Infrastructure.Persistence.Postgres.Entities.MarketCatalogData LegacyRow(
        string marketCode, string language) => new()
        {
            Id = Guid.NewGuid(),
            MarketCode = marketCode,
            DisplayNameKey = $"social.markets.{marketCode.ToLowerInvariant().Replace('-', '_')}",
            PrimaryLanguage = language,
            IsActive = true,
            CreatedAt = Now,
        };

    private static MarketCatalogSeeder Build(AppIdentityDbContext db) =>
        new(db, new TestClock(Now), NullLogger<MarketCatalogSeeder>.Instance);

    private static AppIdentityDbContext CreateDb(string? name = null)
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase(name ?? $"market-catalog-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
