using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentStudio.Operations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Domain.Social.Publications;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Application.Social.ContentStudio;

/// <summary>
/// Deleting a piece must reclaim everything that hangs off it. Nothing in the schema enforces this:
/// set membership, publications and stored assets all reference the item by a soft Guid, so removing
/// only the item row leaves rows pointing at nothing and blobs nobody can reach.
/// </summary>
public sealed class ContentItemPurgeTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Purge_RemovesItem_SetMembership_Publications_AndStoredAssets()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var sets = new EfContentSetRepository(db);
        var publications = new EfPublicationRepository(db);
        var storage = new RecordingAssetStorage();

        var item = await SeedItemAsync(items, assetPath: "org/client/analysis/story.png");
        await SeedSetWithMemberAsync(sets, item.Id);
        // A publication that never reached the network (failed, then abandoned) carries no record
        // worth preserving, so it is purged with the item rather than blocking it.
        await SeedPublicationAsync(publications, item.Id.Value, p =>
        {
            var failed = p.MarkFailed("connector rejected", Now);
            return failed.IsFailure ? failed : p.Cancel(Now);
        });

        var purger = new ContentItemPurger(items, publications, sets, storage);
        var result = await purger.PurgeAsync(Org, item.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await items.GetByIdAsync(Org, item.Id, CancellationToken.None));
        Assert.Empty(await sets.GetSetIdsByItemIdsAsync(Org, [item.Id.Value], CancellationToken.None));
        Assert.Empty(await publications.ListByContentItemsAsync(Org, [item.Id.Value], CancellationToken.None));
        Assert.Equal(["org/client/analysis/story.png"], storage.Deleted);
    }

    [Fact]
    public async Task Purge_IsRefused_WhenItemHasALivePublication()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var sets = new EfContentSetRepository(db);
        var publications = new EfPublicationRepository(db);
        var storage = new RecordingAssetStorage();

        var item = await SeedItemAsync(items, assetPath: "org/client/analysis/story.png");
        await SeedPublicationAsync(publications, item.Id.Value, p => p.MarkPublished("ig-post-1", Now));

        var purger = new ContentItemPurger(items, publications, sets, storage);
        var result = await purger.PurgeAsync(Org, item.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.PurgeBlockedByPublication, result.Error);

        // Nothing may be destroyed on the refused path — not even the blob.
        Assert.NotNull(await items.GetByIdAsync(Org, item.Id, CancellationToken.None));
        Assert.Empty(storage.Deleted);
        Assert.Single(await publications.ListByContentItemsAsync(Org, [item.Id.Value], CancellationToken.None));
    }

    [Fact]
    public async Task Purge_OfAnOrphanedItem_Succeeds_EvenWithNoOwningSet()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var sets = new EfContentSetRepository(db);
        var publications = new EfPublicationRepository(db);

        // The exact shape the Bank reports as orphaned: a real item with no membership row.
        var item = await SeedItemAsync(items);

        var purger = new ContentItemPurger(items, publications, sets);
        var result = await purger.PurgeAsync(Org, item.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await items.GetByIdAsync(Org, item.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Purge_OfAMissingItem_ReportsNotFound()
    {
        await using var db = CreateDbContext();
        var purger = new ContentItemPurger(
            new EfContentItemRepository(db), new EfPublicationRepository(db), new EfContentSetRepository(db));

        var result = await purger.PurgeAsync(Org, ContentItemId.New(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentStudioErrors.ItemNotFound, result.Error);
    }

    [Fact]
    public async Task Purge_SurvivesAStorageFailure_AndStillClearsTheDatabaseRows()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var sets = new EfContentSetRepository(db);
        var publications = new EfPublicationRepository(db);

        var item = await SeedItemAsync(items, assetPath: "org/client/analysis/story.png");

        var purger = new ContentItemPurger(items, publications, sets, new ThrowingAssetStorage());
        var result = await purger.PurgeAsync(Org, item.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await items.GetByIdAsync(Org, item.Id, CancellationToken.None));
    }

    // ─── Seeding ──────────────────────────────────────────────────────────────

    private static async Task<ContentItem> SeedItemAsync(
        EfContentItemRepository items, string? assetPath = null)
    {
        var draft = ContentDraft.ForText("t", new TargetChannel("instagram", "feed"));
        if (assetPath is not null)
        {
            draft = draft with { Assets = [new ContentAssetRef("image", assetPath, "image/png")] };
        }

        var item = ContentItem.Materialize(
            ContentItemId.New(), Org, Client,
            new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, Guid.NewGuid(), "caption"),
            new ContentKind("caption", ContentMedium.Text),
            draft, Now).Value;

        await items.AddRangeAsync([item], CancellationToken.None);
        return item;
    }

    private static async Task SeedSetWithMemberAsync(EfContentSetRepository sets, ContentItemId itemId)
    {
        var set = ContentSet.CreateForRun(
            ContentSetId.New(), Org, Client,
            new SetOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, null),
            Now).Value;

        set.AddMember(itemId, 0);
        await sets.AddAsync(set, CancellationToken.None);
    }

    private static async Task SeedPublicationAsync(
        EfPublicationRepository publications, Guid itemId, Func<Publication, Result> advance)
    {
        var publication = Publication.PublishNow(
            PublicationId.New(), Org, Client, itemId,
            new PublicationDestination(Guid.NewGuid(), Guid.NewGuid(), "instagram"),
            Now).Value;

        Assert.True(advance(publication).IsSuccess);
        await publications.AddAsync(publication, CancellationToken.None);
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"cs-purge-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }

    // ─── Storage doubles ──────────────────────────────────────────────────────

    private class RecordingAssetStorage : IGeneratedAssetStorage
    {
        public List<string> Deleted { get; } = [];

        public virtual Task DeleteAsync(string storagePath, CancellationToken ct)
        {
            Deleted.Add(storagePath);
            return Task.CompletedTask;
        }

        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct) =>
            Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    }

    private sealed class ThrowingAssetStorage : RecordingAssetStorage
    {
        public override Task DeleteAsync(string storagePath, CancellationToken ct) =>
            throw new GeneratedAssetStorageException("storage is down");
    }
}
