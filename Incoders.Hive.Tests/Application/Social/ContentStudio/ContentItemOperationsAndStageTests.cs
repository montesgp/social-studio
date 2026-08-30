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
    public async Task EditCaption_PersistsCaptionAndEditedState()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var item = await SeedItemAsync(items);

        var handler = new EditItemCaptionCommandHandler(items, new FixedClock(Now));
        var result = await handler.Handle(
            new EditItemCaptionCommand(Org, item.Id.Value, "Fresh caption #brand"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Fresh caption #brand", result.Value.Caption);
        Assert.Equal("Edited", result.Value.State);

        var read = await items.GetByIdAsync(Org, item.Id, CancellationToken.None);
        Assert.Equal("Fresh caption #brand", read!.Draft.Caption?.Value);
    }

    [Fact]
    public async Task EditCaption_TooManyHashtags_Fails_AndDoesNotPersist()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var item = await SeedItemAsync(items);

        var tooManyHashtags = string.Join(' ', Enumerable.Range(0, Caption.MaxHashtags + 1).Select(i => $"#t{i}"));
        var handler = new EditItemCaptionCommandHandler(items, new FixedClock(Now));
        var result = await handler.Handle(
            new EditItemCaptionCommand(Org, item.Id.Value, tooManyHashtags), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.CaptionTooManyHashtags, result.Error);

        var read = await items.GetByIdAsync(Org, item.Id, CancellationToken.None);
        Assert.Null(read!.Draft.Caption);
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
    public async Task Stage_ApprovedItem_StopsAtReadyForPublication()
    {
        // Staging used to run the legacy IPublicationHandoff and then mark the item HandedOff, which
        // ADR 0005 superseded when the Publication aggregate became the publication axis. HandedOff
        // is terminal, so a staged piece dropped out of the Planner entirely and could never be
        // published. Staging now just declares the piece ready and leaves it in the flow.
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var item = await SeedItemAsync(items);
        var clock = new FixedClock(Now);

        await new ApproveItemCommandHandler(items, clock)
            .Handle(new ApproveItemCommand(Org, item.Id.Value, Guid.NewGuid()), CancellationToken.None);

        var handler = new StageItemForPublicationCommandHandler(items, clock);
        var result = await handler.Handle(new StageItemForPublicationCommand(Org, item.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ReadyForPublication", result.Value.State);
    }

    [Fact]
    public async Task Stage_ReadyItem_RemainsPublishable()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var item = await SeedItemAsync(items);
        var clock = new FixedClock(Now);

        await new ApproveItemCommandHandler(items, clock)
            .Handle(new ApproveItemCommand(Org, item.Id.Value, Guid.NewGuid()), CancellationToken.None);
        await new StageItemForPublicationCommandHandler(items, clock)
            .Handle(new StageItemForPublicationCommand(Org, item.Id.Value), CancellationToken.None);

        var reloaded = await items.GetByIdAsync(Org, item.Id, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(ReviewState.ReadyForPublication, reloaded!.State);
    }

    [Fact]
    public async Task Stage_NonApprovedItem_Fails()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var item = await SeedItemAsync(items); // Draft

        var handler = new StageItemForPublicationCommandHandler(items, new FixedClock(Now));
        var result = await handler.Handle(new StageItemForPublicationCommand(Org, item.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SpawnVariant_AddsTheVariantToTheSourceItemsOwningSet()
    {
        // Variants used to be created as ORPHANS: the set was looked up by the item's
        // Origin.SourceRunId (a generation JOB id) while a set is keyed by a run id derived from
        // (analysisId, clientId). The lookup silently found nothing, so the variant appeared in the
        // Content Bank but belonged to no set and could not be opened in Studio.
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var sets = new EfContentSetRepository(db);
        var source = await SeedItemAsync(items);
        var set = await SeedSetOwningAsync(sets, source.Id);

        var handler = new SpawnVariantCommandHandler(items, sets, new FixedClock(Now));
        var result = await handler.Handle(
            new SpawnVariantCommand(Org, source.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var owning = await sets.GetSetIdsByItemIdsAsync(Org, [result.Value.Id], CancellationToken.None);
        Assert.True(owning.ContainsKey(result.Value.Id));
        Assert.Equal(set.Id.Value, owning[result.Value.Id]);
    }

    [Fact]
    public async Task DuplicateItem_AddsTheCopyToTheSourceItemsOwningSet()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var sets = new EfContentSetRepository(db);
        var source = await SeedItemAsync(items);
        var set = await SeedSetOwningAsync(sets, source.Id);

        var handler = new DuplicateItemCommandHandler(items, sets, new FixedClock(Now));
        var result = await handler.Handle(
            new DuplicateItemCommand(Org, source.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var owning = await sets.GetSetIdsByItemIdsAsync(Org, [result.Value.Id], CancellationToken.None);
        Assert.Equal(set.Id.Value, owning[result.Value.Id]);
    }

    /// <summary>
    /// Seeds a set that OWNS the given item while deliberately using a run id unrelated to the
    /// item's <c>Origin.SourceRunId</c> — the production shape that the old by-run lookup missed.
    /// </summary>
    private static async Task<ContentSet> SeedSetOwningAsync(EfContentSetRepository sets, ContentItemId itemId)
    {
        var set = ContentSet.CreateForRun(
            ContentSetId.New(), Org, Client,
            SetOrigin.ForRun(GeneratorKind.SingleAnalyzer, Guid.NewGuid()), Now).Value;
        Assert.True(set.AddMember(itemId, 0).IsSuccess);
        await sets.AddAsync(set, CancellationToken.None);
        return set;
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


