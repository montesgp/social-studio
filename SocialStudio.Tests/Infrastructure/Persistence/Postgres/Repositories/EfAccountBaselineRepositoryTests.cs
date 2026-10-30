using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence.Postgres.Repositories;

/// <summary>WU-5: CD-09, design §5.2. Upsert idempotence per (org, network, handle) — no client_id
/// anywhere on this table.</summary>
public sealed class EfAccountBaselineRepositoryTests
{
    private readonly Guid _orgId = Guid.NewGuid();
    private static readonly DateTimeOffset ComputedAt = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpsertAsync_ThenGetAsync_RoundTrips()
    {
        await using var db = CreateDb();
        var repo = new EfAccountBaselineRepository(db);

        await repo.UpsertAsync(_orgId, new AccountBaseline("tiktok", "@a", 1_000, 5, 20, ComputedAt), CancellationToken.None);

        var loaded = await repo.GetAsync(_orgId, "tiktok", "@a", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(1_000, loaded!.MedianViews);
    }

    [Fact]
    public async Task UpsertAsync_CalledTwiceForTheSameAuthor_UpdatesInPlace_NeverDuplicates()
    {
        await using var db = CreateDb();
        var repo = new EfAccountBaselineRepository(db);

        await repo.UpsertAsync(_orgId, new AccountBaseline("tiktok", "@a", 1_000, 5, 20, ComputedAt), CancellationToken.None);
        await repo.UpsertAsync(_orgId, new AccountBaseline("tiktok", "@a", 2_000, 8, 25, ComputedAt.AddDays(31)), CancellationToken.None);

        var rowCount = await db.AccountBaselines.CountAsync();
        Assert.Equal(1, rowCount);

        var loaded = await repo.GetAsync(_orgId, "tiktok", "@a", CancellationToken.None);
        Assert.Equal(2_000, loaded!.MedianViews);
    }

    [Fact]
    public async Task GetAsync_HandleLookupIsCaseInsensitive()
    {
        await using var db = CreateDb();
        var repo = new EfAccountBaselineRepository(db);

        await repo.UpsertAsync(_orgId, new AccountBaseline("tiktok", "@SomeCreator", 1_000, 5, 20, ComputedAt), CancellationToken.None);

        var loaded = await repo.GetAsync(_orgId, "tiktok", "@somecreator", CancellationToken.None);

        Assert.NotNull(loaded);
    }

    [Fact]
    public async Task GetAsync_MissingAuthor_ReturnsNull_NeverFabricated()
    {
        await using var db = CreateDb();
        var repo = new EfAccountBaselineRepository(db);

        var loaded = await repo.GetAsync(_orgId, "tiktok", "@nobody", CancellationToken.None);

        Assert.Null(loaded);
    }

    private static AppIdentityDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"account-baselines-{Guid.NewGuid():N}")
            .Options;

        return new AppIdentityDbContext(options);
    }
}
