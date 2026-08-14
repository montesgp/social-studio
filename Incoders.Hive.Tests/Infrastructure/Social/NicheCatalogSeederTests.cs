using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Infrastructure.Social;
using Incoders.Hive.Tests.Application.Social.Niches;
using Incoders.Hive.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Infrastructure.Social;

/// <summary>
/// The seeder is upsert-on-missing-only: a sysadmin who renames an entry or re-curates its hashtags
/// must not have that overwritten on the next restart.
/// </summary>
public sealed class NicheCatalogSeederTests
{
    private static readonly DateTime Now = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SeedAsync_EmptyCatalog_SeedsTheStarterSet()
    {
        var (seeder, niches) = Build();

        await seeder.SeedAsync(CancellationToken.None);

        var page = await niches.ListAsync(new NicheListQuery(true, 1, 100, null), CancellationToken.None);
        Assert.True(page.TotalCount >= 2);
        Assert.All(page.Items, x => Assert.True(x.IsActive));
    }

    [Theory]
    [InlineData("software-technology", "software", "technology")]
    [InlineData("medical-health", "medical", "health")]
    public async Task SeedAsync_SeedsTheCuratedHashtagsDiscoveryDependsOn(
        string code,
        string firstHashtag,
        string secondHashtag)
    {
        var (seeder, niches) = Build();

        await seeder.SeedAsync(CancellationToken.None);

        var seeded = await niches.GetByCodeAsync(code, CancellationToken.None);
        Assert.NotNull(seeded);
        Assert.Equal(new[] { firstHashtag, secondHashtag }, seeded!.SearchHashtags);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        var (seeder, niches) = Build();

        await seeder.SeedAsync(CancellationToken.None);
        var afterFirst = await niches.ListAsync(new NicheListQuery(true, 1, 100, null), CancellationToken.None);

        await seeder.SeedAsync(CancellationToken.None);
        var afterSecond = await niches.ListAsync(new NicheListQuery(true, 1, 100, null), CancellationToken.None);

        Assert.Equal(afterFirst.TotalCount, afterSecond.TotalCount);
    }

    [Fact]
    public async Task SeedAsync_PinsDeterministicIdsAcrossRuns()
    {
        var (firstSeeder, firstStore) = Build();
        var (secondSeeder, secondStore) = Build();

        await firstSeeder.SeedAsync(CancellationToken.None);
        await secondSeeder.SeedAsync(CancellationToken.None);

        var first = await firstStore.GetByCodeAsync("software-technology", CancellationToken.None);
        var second = await secondStore.GetByCodeAsync("software-technology", CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(first!.Id, second!.Id);
    }

    [Fact]
    public async Task SeedAsync_DoesNotOverwriteASysadminEdit()
    {
        var (seeder, niches) = Build();
        await seeder.SeedAsync(CancellationToken.None);

        var seeded = await niches.GetByCodeAsync("software-technology", CancellationToken.None);
        Assert.True(seeded!.Update("Software, Technology & AI", "chip", ["software", "ai"], Now).IsSuccess);
        niches.Update(seeded);

        await seeder.SeedAsync(CancellationToken.None);

        var reread = await niches.GetByCodeAsync("software-technology", CancellationToken.None);
        Assert.Equal("Software, Technology & AI", reread!.DisplayName);
        Assert.Equal(new[] { "software", "ai" }, reread.SearchHashtags);
    }

    private static (NicheCatalogSeeder Seeder, InMemoryNicheRepository Niches) Build()
    {
        var niches = new InMemoryNicheRepository();
        var seeder = new NicheCatalogSeeder(
            niches,
            new TestClock(Now),
            NullLogger<NicheCatalogSeeder>.Instance);

        return (seeder, niches);
    }
}
