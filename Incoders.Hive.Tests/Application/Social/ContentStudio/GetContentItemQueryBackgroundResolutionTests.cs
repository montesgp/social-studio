using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentStudio.Queries;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Tests.Application.Social.ContentStudio;

/// <summary>
/// Issue #58 end-to-end: GetContentItemQuery must return studio markup with the AI-generated
/// background inlined as a data-URI when the persisted envelope carries a storage-path
/// <c>backgroundImageRef</c> (root cause of the broken studio preview after #56).
/// </summary>
public sealed class GetContentItemQueryBackgroundResolutionTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 11, 12, 0, 0, DateTimeKind.Utc);
    private const string StoragePath = "org/client/analysis/feed-carousel/slide_1_bg.png";

    [Fact]
    public async Task Handle_StoragePathBackground_ReturnsMarkupWithDataUri()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var markup = BuildEnvelopeJson(StoragePath);
        var item = VisualItem(markup);
        await items.AddRangeAsync(new[] { item }, CancellationToken.None);
        await db.SaveChangesAsync();

        var storage = new FakeGeneratedAssetStorage(StoragePath, new byte[] { 9, 9, 9 }, "image/png");
        var handler = new GetContentItemQueryHandler(items, storage);

        var result = await handler.Handle(new GetContentItemQuery(Org, item.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("data:image/png;base64,", result.Value.VisualSpecMarkup);
        Assert.DoesNotContain(StoragePath, result.Value.VisualSpecMarkup);
    }

    [Fact]
    public async Task Handle_NoAssetStorageWired_ReturnsMarkupUnchanged()
    {
        await using var db = CreateDbContext();
        var items = new EfContentItemRepository(db);
        var markup = BuildEnvelopeJson(StoragePath);
        var item = VisualItem(markup);
        await items.AddRangeAsync(new[] { item }, CancellationToken.None);
        await db.SaveChangesAsync();

        var handler = new GetContentItemQueryHandler(items, assetStorage: null);

        var result = await handler.Handle(new GetContentItemQuery(Org, item.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(markup, result.Value.VisualSpecMarkup);
    }

    private static string BuildEnvelopeJson(string backgroundRef) =>
        "{\"schemaVersion\":1,\"markup\":\"<img src=\\\"" + backgroundRef + "\\\" />\",\"canvas\":{\"width\":1080,\"height\":1080,\"dpi\":96},\"slotGeometry\":[],\"backgroundImageRef\":\"" + backgroundRef + "\"}";

    private static ContentItem VisualItem(string markup)
    {
        var id = ContentItemId.New();
        var draft = new ContentDraft(null, null, markup, Array.Empty<ContentAssetRef>(), new TargetChannel("instagram", "carousel"));
        return ContentItem.Materialize(
            id, Org, Client,
            new ContentOrigin(GeneratorKind.SingleAnalyzer, Guid.NewGuid(), null, Guid.NewGuid(), "carousel"),
            new ContentKind("carousel", ContentMedium.Visual), draft, Now).Value;
    }

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"cs-item-bg-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }
}

file sealed class FakeGeneratedAssetStorage(string expectedPath, byte[] bytes, string mime) : IGeneratedAssetStorage
{
    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        if (!string.Equals(storagePath, expectedPath, StringComparison.Ordinal))
        {
            return Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
        }

        return Task.FromResult(Result.Success(new StoredAssetStream(new MemoryStream(bytes), mime, bytes.Length)));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}
