using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Features.Social.ContentStudio.Operations;
using Incoders.Hive.Application.Features.Social.ContentStudio.Stage;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Application.Social.ContentStudio;

public sealed class ContentItemOperationsAndStageTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Approve_FromDraft_PersistsApprovedAndReviewer()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var item = await SeedItemAsync(items);
        var reviewer = Guid.NewGuid();

        var handler = new ApproveItemCommandHandler(items, new FixedClock(Now));
        var result = await handler.Handle(new ApproveItemCommand(Org, item.Id.Value, reviewer), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Approved", result.Value.State);

        var read = await items.GetByIdAsync(Org, item.Id, CancellationToken.None);
        Assert.Equal(ReviewState.Approved, read!.State);
        Assert.Equal(reviewer, read.ReviewerUserId);
    }

    [Fact]
    public async Task EditText_PersistsDraftAndEditedState()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var item = await SeedItemAsync(items, text: "before");

        var handler = new EditItemTextCommandHandler(items, new FixedClock(Now));
        var result = await handler.Handle(new EditItemTextCommand(Org, item.Id.Value, "after"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("after", result.Value.Text);
        Assert.Equal("Edited", result.Value.State);
    }

    [Fact]
    public async Task Reject_WithoutReason_Fails()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var item = await SeedItemAsync(items);

        var handler = new RejectItemCommandHandler(items, new FixedClock(Now));
        var result = await handler.Handle(new RejectItemCommand(Org, item.Id.Value, Guid.NewGuid(), "  "), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.RejectionReasonRequired, result.Error);
    }

    [Fact]
    public async Task Stage_ApprovedItem_EmitsHandoff_AndMarksHandedOff()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var item = await SeedItemAsync(items);
        var clock = new FixedClock(Now);

        await new ApproveItemCommandHandler(items, clock)
            .Handle(new ApproveItemCommand(Org, item.Id.Value, Guid.NewGuid()), CancellationToken.None);

        var handoff = new RecordingHandoff();
        var handler = new StageItemForPublicationCommandHandler(items, handoff, clock);
        var result = await handler.Handle(new StageItemForPublicationCommand(Org, item.Id.Value, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("HandedOff", result.Value.State);
        Assert.NotNull(handoff.LastPayload);
        Assert.Equal(PublicationHandoffPayload.CurrentSchemaVersion, handoff.LastPayload!.SchemaVersion);
        Assert.Equal(item.Id.Value, handoff.LastPayload.ContentItemId);
    }

    [Fact]
    public async Task Stage_NonApprovedItem_Fails_AndEmitsNoHandoff()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var item = await SeedItemAsync(items); // Draft

        var handoff = new RecordingHandoff();
        var handler = new StageItemForPublicationCommandHandler(items, handoff, new FixedClock(Now));
        var result = await handler.Handle(new StageItemForPublicationCommand(Org, item.Id.Value, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(handoff.LastPayload);
    }

    private static async Task<ContentItem> SeedItemAsync(EfContentItemRepository items, string text = "caption")
    {
        var id = ContentItemId.New();
        var origin = new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, Guid.NewGuid(), "caption");
        var item = ContentItem.Materialize(
            id, Org, Client, origin, new ContentKind("caption", ContentMedium.Text),
            ContentDraft.ForText(text, new TargetChannel("instagram", "feed")), Now).Value;
        await items.AddRangeAsync(new[] { item }, CancellationToken.None);
        return item;
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"cs-ops-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}

file sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; } = utcNow;
}

file sealed class RecordingHandoff : IPublicationHandoff
{
    public PublicationHandoffPayload? LastPayload { get; private set; }

    public Task<Result> HandoffAsync(PublicationHandoffPayload payload, CancellationToken ct)
    {
        LastPayload = payload;
        return Task.FromResult(Result.Success());
    }
}
