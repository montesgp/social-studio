using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Social.NicheDiscovery;
using Incoders.Hive.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// WU-7a: ND-14. The seeder is upsert-on-missing-only, mirroring <c>NicheCatalogSeederTests</c> — an
/// idempotent re-run must not duplicate the seeded <c>es-AR</c> row, and its GUID is deterministic
/// across independently constructed seeders.
/// </summary>
public sealed class MarketCatalogSeederTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SeedAsync_EmptyCatalog_SeedsTheEsArRow()
    {
        await using var db = CreateDb();
        var seeder = Build(db);

        await seeder.SeedAsync(CancellationToken.None);

        var seeded = await db.MarketCatalog.SingleOrDefaultAsync(x => x.MarketCode == "es-AR");
        Assert.NotNull(seeded);
        Assert.True(seeded!.IsActive);
        Assert.Equal("social.markets.es_ar", seeded.DisplayNameKey);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_DoesNotDuplicateTheRow()
    {
        await using var db = CreateDb();
        var seeder = Build(db);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        var rowCount = await db.MarketCatalog.CountAsync(x => x.MarketCode == "es-AR");
        Assert.Equal(1, rowCount);
    }

    [Fact]
    public async Task SeedAsync_PinsADeterministicIdAcrossIndependentSeeders()
    {
        await using var firstDb = CreateDb();
        await Build(firstDb).SeedAsync(CancellationToken.None);
        var firstSeeded = await firstDb.MarketCatalog.SingleAsync(x => x.MarketCode == "es-AR");

        await using var secondDb = CreateDb();
        await Build(secondDb).SeedAsync(CancellationToken.None);
        var secondSeeded = await secondDb.MarketCatalog.SingleAsync(x => x.MarketCode == "es-AR");

        Assert.Equal(firstSeeded.Id, secondSeeded.Id);
    }

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
