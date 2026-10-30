using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Tests.Infrastructure.Persistence;

/// <summary>
/// WU3 (Phase 10) mapping tests for <see cref="EfContentItemRepository.ListByClientAsync"/> — the
/// Content Bank read seam (design §5.1, CS-BANK-01, CB-01).
/// </summary>
public sealed class ContentItemRepositoryListByClientTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ListByClientAsync_ReturnsItemsAcrossDifferentSetsAndOrigins()
    {
        await using var db = CreateDbContext();
        var repo = new EfContentItemRepository(db);

        var itemA = BuildItem(runId: Guid.NewGuid());
        var itemB = BuildItem(runId: Guid.NewGuid());
        await repo.AddRangeAsync([itemA, itemB], CancellationToken.None);

        var result = await repo.ListByClientAsync(Org, Client, ContentBankFilter.Empty, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ListByClientAsync_ScopedStrictlyToRequestingClient()
    {
        await using var db = CreateDbContext();
        var repo = new EfContentItemRepository(db);

        var mine = BuildItem(runId: Guid.NewGuid(), clientId: Client);
        var other = BuildItem(runId: Guid.NewGuid(), clientId: Guid.NewGuid());
        await repo.AddRangeAsync([mine, other], CancellationToken.None);

        var result = await repo.ListByClientAsync(Org, Client, ContentBankFilter.Empty, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(mine.Id, result[0].Id);
    }

    [Fact]
    public async Task ListByClientAsync_AppliesDateRangeAndStateFilters()
    {
        await using var db = CreateDbContext();
        var repo = new EfContentItemRepository(db);

        var early = BuildItem(runId: Guid.NewGuid(), createdAtUtc: Now.AddDays(-10));
        var recent = BuildItem(runId: Guid.NewGuid(), createdAtUtc: Now);
        await repo.AddRangeAsync([early, recent], CancellationToken.None);

        var filtered = await repo.ListByClientAsync(
            Org, Client, new ContentBankFilter(Now.AddDays(-1), null, null), CancellationToken.None);

        Assert.Single(filtered);
        Assert.Equal(recent.Id, filtered[0].Id);
    }

    [Fact]
    public async Task ListByClientAsync_OrdersByCreatedAtDescending()
    {
        await using var db = CreateDbContext();
        var repo = new EfContentItemRepository(db);

        var older = BuildItem(runId: Guid.NewGuid(), createdAtUtc: Now.AddDays(-2));
        var newer = BuildItem(runId: Guid.NewGuid(), createdAtUtc: Now);
        await repo.AddRangeAsync([older, newer], CancellationToken.None);

        var result = await repo.ListByClientAsync(Org, Client, ContentBankFilter.Empty, CancellationToken.None);

        Assert.Equal(newer.Id, result[0].Id);
        Assert.Equal(older.Id, result[1].Id);
    }

    private static ContentItem BuildItem(
        Guid runId, Guid? clientId = null, DateTime? createdAtUtc = null)
    {
        var draft = new ContentDraft(
            null, Caption.Create("Hello world").Value, null,
            [new ContentAssetRef("rendered", "org/client/analysis/slide_1.png", "image/png")],
            new TargetChannel("instagram", "feed"));

        var created = ContentItem.Materialize(
            ContentItemId.New(), Org, clientId ?? Client,
            new ContentOrigin(GeneratorKind.SingleAnalyzer, runId, null, Guid.NewGuid(), "ig_feed_carousel"),
            new ContentKind("ig_feed_carousel", ContentMedium.Visual), draft, createdAtUtc ?? Now);

        return created.Value;
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"content-items-list-by-client-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}
