using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Features.Social.ContentStudio.Bulk;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Application.Social.ContentStudio;

public sealed class BulkItemOperationCommandHandlerTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task BulkApprove_IsPartialSuccess_TerminalAndMissingItemsFail()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);

        var ok1 = await SeedAsync(items);
        var ok2 = await SeedAsync(items);
        var discarded = await SeedAsync(items, discard: true); // Approve from Discarded is illegal
        var missing = Guid.NewGuid();

        var handler = new BulkItemOperationCommandHandler(items, new FixedClock(Now));
        var result = await handler.Handle(
            new BulkItemOperationCommand(
                Org, Guid.NewGuid(),
                new[] { ok1.Id.Value, ok2.Id.Value, discarded.Id.Value, missing },
                BulkItemOperation.Approve, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Total);
        Assert.Equal(2, result.Value.Succeeded);
        Assert.Equal(2, result.Value.Failed);

        Assert.Equal(ReviewState.Approved, (await items.GetByIdAsync(Org, ok1.Id, CancellationToken.None))!.State);
        Assert.Equal(ReviewState.Discarded, (await items.GetByIdAsync(Org, discarded.Id, CancellationToken.None))!.State);
    }

    [Fact]
    public async Task BulkReject_WithoutReason_Fails()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var item = await SeedAsync(items);

        var handler = new BulkItemOperationCommandHandler(items, new FixedClock(Now));
        var result = await handler.Handle(
            new BulkItemOperationCommand(Org, Guid.NewGuid(), new[] { item.Id.Value }, BulkItemOperation.Reject, "  "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.RejectionReasonRequired, result.Error);
    }

    [Fact]
    public async Task BulkDelete_RemovesItems()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var a = await SeedAsync(items);
        var b = await SeedAsync(items);

        var handler = new BulkItemOperationCommandHandler(items, new FixedClock(Now));
        var result = await handler.Handle(
            new BulkItemOperationCommand(Org, Guid.NewGuid(), new[] { a.Id.Value, b.Id.Value }, BulkItemOperation.Delete, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Succeeded);
        Assert.Null(await items.GetByIdAsync(Org, a.Id, CancellationToken.None));
        Assert.Null(await items.GetByIdAsync(Org, b.Id, CancellationToken.None));
    }

    private static async Task<ContentItem> SeedAsync(EfContentItemRepository items, bool discard = false)
    {
        var id = ContentItemId.New();
        var origin = new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, Guid.NewGuid(), "caption");
        var item = ContentItem.Materialize(
            id, Org, Client, origin, new ContentKind("caption", ContentMedium.Text),
            ContentDraft.ForText("t", new TargetChannel("instagram", "feed")), Now).Value;
        if (discard)
        {
            item.Discard(Now);
        }

        await items.AddRangeAsync(new[] { item }, CancellationToken.None);
        return item;
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"cs-bulk-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}

file sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; } = utcNow;
}
