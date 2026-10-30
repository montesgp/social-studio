using SocialStudio.Domain.Social.NicheDiscovery;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using SocialStudio.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>WU-7b: ND-14. Read-only listing over the seeded <c>social.market_catalog</c> table.</summary>
public sealed class EfMarketCatalogRepositoryTests
{
    [Fact]
    public async Task ListActiveAsync_AfterSeeding_ReturnsEsAr()
    {
        await using var db = CreateDb();
        Seed(db, "es-AR", isActive: true);
        await db.SaveChangesAsync();
        var repo = new EfMarketCatalogRepository(db);

        var active = await repo.ListActiveAsync(CancellationToken.None);

        Assert.Contains(active, x => x.Code.Value == "es-AR");
    }

    [Fact]
    public async Task ListActiveAsync_ExcludesInactiveEntries()
    {
        await using var db = CreateDb();
        Seed(db, "es-AR", isActive: true);
        Seed(db, "pt-BR", isActive: false);
        await db.SaveChangesAsync();
        var repo = new EfMarketCatalogRepository(db);

        var active = await repo.ListActiveAsync(CancellationToken.None);

        Assert.DoesNotContain(active, x => x.Code.Value == "pt-BR");
    }

    [Fact]
    public async Task GetByCodeAsync_MissingCode_ReturnsNull()
    {
        await using var db = CreateDb();
        var repo = new EfMarketCatalogRepository(db);

        var result = await repo.GetByCodeAsync(MarketCode.TryParse("pt-BR").Value, CancellationToken.None);

        Assert.Null(result);
    }

    private static void Seed(AppIdentityDbContext db, string marketCode, bool isActive)
    {
        db.MarketCatalog.Add(new MarketCatalogData
        {
            Id = Guid.NewGuid(),
            MarketCode = marketCode,
            DisplayNameKey = "social.markets.test",
            PrimaryLanguage = marketCode[..2],
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"market-catalog-repo-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
