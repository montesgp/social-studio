using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using SkiaSharp;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// WU-7b (D13): <see cref="SkiaBrandMarkAssetExtractor"/> crops an icon-type brand mark out of an
/// archived source image and persists it via <see cref="IGeneratedAssetStorage"/>. Contract: NEVER
/// throws — every failure mode (source open, decode, invalid box, sub-24x24 crop, storage) maps to
/// <c>Result.Failure</c> so the caller degrades to the PositionHint+ColorHex chip.
/// </summary>
public sealed class SkiaBrandMarkAssetExtractorTests
{
    private static readonly StoredAssetKey TargetKey = new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "brand-mark.png");

    private static byte[] Png(int width, int height, SKColor fill, Action<SKCanvas>? paint = null)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(fill);
            paint?.Invoke(canvas);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task ExtractAsync_ValidBoxOnDecodableImage_CropsCorrectPixelRectAndStores()
    {
        // 200x100 source: left half red, right half blue. Box selects the right half exactly.
        var sourceBytes = Png(200, 100, SKColors.Red, canvas =>
        {
            using var paint = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(SKRect.Create(100, 0, 100, 100), paint);
        });
        var storage = new FakeGeneratedAssetStorage(("clients/ref.png", sourceBytes));
        var extractor = new SkiaBrandMarkAssetExtractor(storage);
        var box = new BrandMarkBoundingBox(0.5, 0.0, 0.5, 1.0); // right half, normalised

        var result = await extractor.ExtractAsync(TargetKey, "clients/ref.png", box, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("brand-mark.png", storage.LastPutKey!.RelativePath);
        using var cropped = SKBitmap.Decode(storage.LastPutContent!.Bytes);
        Assert.Equal(100, cropped.Width);
        Assert.Equal(100, cropped.Height);
        // Every sampled pixel in the crop must be the blue half's color, proving the CORRECT region
        // was sliced (not a re-encode of the whole source or the left/red half).
        Assert.Equal(SKColors.Blue, cropped.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, cropped.GetPixel(99, 99));
    }

    [Fact]
    public async Task ExtractAsync_SourceImageNotFound_ReturnsFailureNeverThrows()
    {
        var storage = new FakeGeneratedAssetStorage(); // no seeded source → OpenAsync fails
        var extractor = new SkiaBrandMarkAssetExtractor(storage);
        var box = new BrandMarkBoundingBox(0.1, 0.1, 0.5, 0.5);

        var result = await extractor.ExtractAsync(TargetKey, "missing.png", box, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(storage.LastPutKey);
    }

    [Fact]
    public async Task ExtractAsync_UndecodableBytes_ReturnsFailureNeverThrows()
    {
        var storage = new FakeGeneratedAssetStorage(("clients/ref.png", [0x00, 0x01, 0x02, 0x03]));
        var extractor = new SkiaBrandMarkAssetExtractor(storage);
        var box = new BrandMarkBoundingBox(0.1, 0.1, 0.5, 0.5);

        var result = await extractor.ExtractAsync(TargetKey, "clients/ref.png", box, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(storage.LastPutKey);
    }

    [Fact]
    public async Task ExtractAsync_BoxScalesBelowMinimumCropSize_ReturnsFailureNeverThrows()
    {
        // 100x100 source; a 10%x10% box scales to 10x10px — below the 24x24 minimum crop guard.
        var sourceBytes = Png(100, 100, SKColors.Green);
        var storage = new FakeGeneratedAssetStorage(("clients/ref.png", sourceBytes));
        var extractor = new SkiaBrandMarkAssetExtractor(storage);
        var box = new BrandMarkBoundingBox(0.0, 0.0, 0.1, 0.1);

        var result = await extractor.ExtractAsync(TargetKey, "clients/ref.png", box, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(storage.LastPutKey);
    }

    [Fact]
    public async Task ExtractAsync_BoxAtMinimumCropSize_Succeeds()
    {
        // 240x240 source; a 10%x10% box scales to exactly 24x24px — clears the minimum guard.
        var sourceBytes = Png(240, 240, SKColors.Green);
        var storage = new FakeGeneratedAssetStorage(("clients/ref.png", sourceBytes));
        var extractor = new SkiaBrandMarkAssetExtractor(storage);
        var box = new BrandMarkBoundingBox(0.0, 0.0, 0.1, 0.1);

        var result = await extractor.ExtractAsync(TargetKey, "clients/ref.png", box, CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var cropped = SKBitmap.Decode(storage.LastPutContent!.Bytes);
        Assert.Equal(24, cropped.Width);
        Assert.Equal(24, cropped.Height);
    }

    [Fact]
    public async Task ExtractAsync_StoragePutThrows_ReturnsFailureNeverThrows()
    {
        var sourceBytes = Png(200, 200, SKColors.Green);
        var storage = new FakeGeneratedAssetStorage(("clients/ref.png", sourceBytes)) { ThrowOnPut = true };
        var extractor = new SkiaBrandMarkAssetExtractor(storage);
        var box = new BrandMarkBoundingBox(0.0, 0.0, 0.5, 0.5);

        var result = await extractor.ExtractAsync(TargetKey, "clients/ref.png", box, CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}

/// <summary>Minimal in-memory <see cref="IGeneratedAssetStorage"/> fake for extractor tests.</summary>
file sealed class FakeGeneratedAssetStorage : IGeneratedAssetStorage
{
    private readonly Dictionary<string, byte[]> _objects;

    public bool ThrowOnPut { get; init; }

    public StoredAssetKey? LastPutKey { get; private set; }

    public StoredAssetContent? LastPutContent { get; private set; }

    public FakeGeneratedAssetStorage(params (string path, byte[] bytes)[] seeded)
    {
        _objects = seeded.ToDictionary(s => s.path, s => s.bytes);
    }

    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
    {
        if (ThrowOnPut)
        {
            throw new GeneratedAssetStorageException("simulated PUT failure");
        }

        LastPutKey = key;
        LastPutContent = content;
        return Task.FromResult(new StoredAssetRef(
            $"{key.OrganizationId}/{key.ClientId}/{key.AnalysisId}/{key.RelativePath}",
            content.Mime,
            content.Bytes.Length,
            "hash"));
    }

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        if (!_objects.TryGetValue(storagePath, out var bytes))
        {
            return Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
        }

        var stream = new StoredAssetStream(new MemoryStream(bytes), "image/png", bytes.Length);
        return Task.FromResult(Result.Success(stream));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}
