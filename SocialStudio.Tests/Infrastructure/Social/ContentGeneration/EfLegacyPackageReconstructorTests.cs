using System.Text;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using SocialStudio.Infrastructure.Social.ContentGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Tests for the one-time legacy package reconstruction migration: re-keys the
/// format taxonomy and renames stored objects, idempotently, with collision safety.
/// </summary>
public sealed class EfLegacyPackageReconstructorTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static string Prefix => $"{OrgId}/{ClientId}/{AnalysisId}";

    [Fact]
    public async Task Reconstruct_LegacyStorySingle_ReKeysAndRenamesObject()
    {
        await using var db = CreateDbContext();
        var storage = new FakeStorage();
        var resultId = Guid.NewGuid();

        SeedResult(db, resultId, "story");
        SeedAsset(db, resultId, index: 0, relative: "story.png");
        storage.Put($"{Prefix}/story.png", "image/png", "story-bytes");
        await db.SaveChangesAsync();

        var report = await CreateSut(db, storage).ReconstructAsync(CancellationToken.None);

        Assert.Equal(1, report.ResultsReKeyed);
        Assert.Equal(1, report.AssetsRenamed);
        Assert.Equal(0, report.StaleResultsDeleted);

        var result = await db.ContentGenerationFormatResults.SingleAsync();
        Assert.Equal("story-single", result.FormatKey);

        var asset = await db.ContentGenerationFormatAssets.SingleAsync();
        Assert.Equal($"{Prefix}/story-single.png", asset.StoragePath);
        Assert.Equal("story-single.png", asset.Filename);

        Assert.False(storage.Exists($"{Prefix}/story.png"));
        Assert.True(storage.Exists($"{Prefix}/story-single.png"));
        Assert.Equal("story-bytes", storage.ReadText($"{Prefix}/story-single.png"));
    }

    [Fact]
    public async Task Reconstruct_LegacyCarousel_ReKeysToFeedCarouselAndMovesSlides()
    {
        await using var db = CreateDbContext();
        var storage = new FakeStorage();
        var resultId = Guid.NewGuid();

        SeedResult(db, resultId, "carousel");
        SeedAsset(db, resultId, index: 1, relative: "carousel/slide_1.png");
        SeedAsset(db, resultId, index: 2, relative: "carousel/slide_2.png");
        storage.Put($"{Prefix}/carousel/slide_1.png", "image/png", "s1");
        storage.Put($"{Prefix}/carousel/slide_2.png", "image/png", "s2");
        await db.SaveChangesAsync();

        var report = await CreateSut(db, storage).ReconstructAsync(CancellationToken.None);

        Assert.Equal(1, report.ResultsReKeyed);
        Assert.Equal(2, report.AssetsRenamed);

        var result = await db.ContentGenerationFormatResults.SingleAsync();
        Assert.Equal("feed-carousel", result.FormatKey);

        var paths = await db.ContentGenerationFormatAssets.Select(a => a.StoragePath).OrderBy(p => p).ToListAsync();
        Assert.Equal(
            [$"{Prefix}/feed-carousel/slide_1.png", $"{Prefix}/feed-carousel/slide_2.png"],
            paths);
        Assert.True(storage.Exists($"{Prefix}/feed-carousel/slide_1.png"));
        Assert.False(storage.Exists($"{Prefix}/carousel/slide_1.png"));
    }

    [Fact]
    public async Task Reconstruct_FeedPost_ReKeysToFeedSingle()
    {
        await using var db = CreateDbContext();
        var storage = new FakeStorage();
        var resultId = Guid.NewGuid();

        SeedResult(db, resultId, "feed-post");
        SeedAsset(db, resultId, index: 0, relative: "feed-post.png");
        storage.Put($"{Prefix}/feed-post.png", "image/png", "fp");
        await db.SaveChangesAsync();

        await CreateSut(db, storage).ReconstructAsync(CancellationToken.None);

        var result = await db.ContentGenerationFormatResults.SingleAsync();
        Assert.Equal("feed-single", result.FormatKey);
        var asset = await db.ContentGenerationFormatAssets.SingleAsync();
        Assert.Equal($"{Prefix}/feed-single.png", asset.StoragePath);
    }

    [Fact]
    public async Task Reconstruct_RunTwice_SecondRunIsNoOp()
    {
        await using var db = CreateDbContext();
        var storage = new FakeStorage();
        var resultId = Guid.NewGuid();

        SeedResult(db, resultId, "story");
        SeedAsset(db, resultId, index: 0, relative: "story.png");
        storage.Put($"{Prefix}/story.png", "image/png", "story-bytes");
        await db.SaveChangesAsync();

        var sut = CreateSut(db, storage);
        await sut.ReconstructAsync(CancellationToken.None);
        var second = await sut.ReconstructAsync(CancellationToken.None);

        Assert.True(second.NoOp);
        var result = await db.ContentGenerationFormatResults.SingleAsync();
        Assert.Equal("story-single", result.FormatKey);
    }

    [Fact]
    public async Task Reconstruct_LegacyCollidesWithCanonical_DropsStaleLegacy()
    {
        await using var db = CreateDbContext();
        var storage = new FakeStorage();
        var legacyResultId = Guid.NewGuid();
        var canonicalResultId = Guid.NewGuid();

        // Canonical regeneration already owns story-single.png
        SeedResult(db, canonicalResultId, "story-single");
        SeedAsset(db, canonicalResultId, index: 0, relative: "story-single.png");
        storage.Put($"{Prefix}/story-single.png", "image/png", "new-bytes");

        // Stale legacy story still present
        SeedResult(db, legacyResultId, "story");
        SeedAsset(db, legacyResultId, index: 0, relative: "story.png");
        storage.Put($"{Prefix}/story.png", "image/png", "old-bytes");
        await db.SaveChangesAsync();

        var report = await CreateSut(db, storage).ReconstructAsync(CancellationToken.None);

        Assert.Equal(1, report.StaleResultsDeleted);
        Assert.Equal(0, report.ResultsReKeyed);

        // Only the canonical result remains; its object is untouched.
        var result = await db.ContentGenerationFormatResults.SingleAsync();
        Assert.Equal(canonicalResultId, result.Id);
        Assert.Equal("new-bytes", storage.ReadText($"{Prefix}/story-single.png"));
        Assert.False(storage.Exists($"{Prefix}/story.png"));
        Assert.Single(await db.ContentGenerationFormatAssets.ToListAsync());
    }

    // ─── Seeding helpers ──────────────────────────────────────────────────────

    private static void SeedResult(AppIdentityDbContext db, Guid resultId, string formatKey) =>
        db.ContentGenerationFormatResults.Add(new ContentGenerationFormatResultData
        {
            Id = resultId,
            JobId = Guid.NewGuid(),
            FormatKey = formatKey,
            Status = "Succeeded",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

    private static void SeedAsset(AppIdentityDbContext db, Guid resultId, int index, string relative) =>
        db.ContentGenerationFormatAssets.Add(new ContentGenerationFormatAssetData
        {
            Id = Guid.NewGuid(),
            FormatResultId = resultId,
            AssetIndex = index,
            StoragePath = $"{OrgId}/{ClientId}/{AnalysisId}/{relative}",
            Filename = relative.Split('/')[^1],
            MimeType = "image/png",
            SizeBytes = 10,
            ContentHash = "hash",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

    private static EfLegacyPackageReconstructor CreateSut(AppIdentityDbContext db, FakeStorage storage) =>
        new(db, storage, NullLogger<EfLegacyPackageReconstructor>.Instance);

    private static AppIdentityDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseInMemoryDatabase($"recon-{Guid.NewGuid():N}")
            .Options;
        return new AppIdentityDbContext(options);
    }

    // ─── In-memory storage fake ───────────────────────────────────────────────

    private sealed class FakeStorage : IGeneratedAssetStorage
    {
        private readonly Dictionary<string, (byte[] Bytes, string Mime)> _objects = new(StringComparer.Ordinal);

        public void Put(string path, string mime, string text) =>
            _objects[path] = (Encoding.UTF8.GetBytes(text), mime);

        public bool Exists(string path) => _objects.ContainsKey(path);

        public string ReadText(string path) => Encoding.UTF8.GetString(_objects[path].Bytes);

        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
        {
            var path = $"{key.OrganizationId}/{key.ClientId}/{key.AnalysisId}/{key.RelativePath}";
            _objects[path] = (content.Bytes, content.Mime);
            return Task.FromResult(new StoredAssetRef(path, content.Mime, content.Bytes.Length, "hash"));
        }

        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
        {
            var entries = _objects
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(kv => new StoredAssetEntry(kv.Key, kv.Key, kv.Value.Bytes.Length, kv.Value.Mime, null, false))
                .ToList();
            return Task.FromResult<IReadOnlyList<StoredAssetEntry>>(entries);
        }

        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
        {
            if (!_objects.TryGetValue(storagePath, out var obj))
            {
                return Task.FromResult(Result<StoredAssetStream>.Failure(StorageErrors.NotFound));
            }

            var stream = new StoredAssetStream(new MemoryStream(obj.Bytes), obj.Mime, obj.Bytes.Length);
            return Task.FromResult(Result<StoredAssetStream>.Success(stream));
        }

        public Task DeleteAsync(string storagePath, CancellationToken ct)
        {
            _objects.Remove(storagePath);
            return Task.CompletedTask;
        }
    }
}
