using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using SkiaSharp;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Production <see cref="IBrandMarkAssetExtractor"/>: crops an icon-type brand mark out of the
/// archived source image via SkiaSharp and persists the crop through <see cref="IGeneratedAssetStorage"/>
/// (WU-7b, D13). Crop-only — no matting, no vision API call, no logo regeneration.
///
/// <para>
/// Same SkiaSharp-behind-a-port shape as <see cref="DegenerateImageGuardrailValidator"/> (F4):
/// decode bytes the pipeline itself already archived, inspect/transform them, never throw.
/// </para>
/// </summary>
public sealed class SkiaBrandMarkAssetExtractor : IBrandMarkAssetExtractor
{
    /// <summary>Minimum crop dimension, in pixels, after scaling the normalised box to the source image (design.md).</summary>
    public const int MinimumCropDimensionPx = 24;

    private static readonly Error SourceOpenFailed =
        Error.Failure("brand-mark-asset.source-open-failed", "brandMarkAsset.sourceOpenFailed");

    private static readonly Error DecodeFailed =
        Error.Failure("brand-mark-asset.decode-failed", "brandMarkAsset.decodeFailed");

    private static readonly Error CropTooSmall =
        Error.Failure("brand-mark-asset.crop-too-small", "brandMarkAsset.cropTooSmall");

    private static readonly Error StoreFailed =
        Error.Failure("brand-mark-asset.store-failed", "brandMarkAsset.storeFailed");

    private static readonly Error Unexpected =
        Error.Failure("brand-mark-asset.unexpected", "brandMarkAsset.unexpected");

    private readonly IGeneratedAssetStorage _storage;

    public SkiaBrandMarkAssetExtractor(IGeneratedAssetStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
    }

    public async Task<Result<StoredAssetRef>> ExtractAsync(
        StoredAssetKey targetKey,
        string sourceImagePath,
        BrandMarkBoundingBox box,
        CancellationToken ct)
    {
        try
        {
            var openResult = await _storage.OpenAsync(sourceImagePath, ct);
            if (openResult.IsFailure)
            {
                return Result.Failure<StoredAssetRef>(SourceOpenFailed);
            }

            using var sourceStream = openResult.Value;
            using var bitmap = SKBitmap.Decode(sourceStream.Content);
            if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
            {
                return Result.Failure<StoredAssetRef>(DecodeFailed);
            }

            var cropRect = ScaleToPixelRect(box, bitmap.Width, bitmap.Height);
            if (cropRect.Width < MinimumCropDimensionPx || cropRect.Height < MinimumCropDimensionPx)
            {
                return Result.Failure<StoredAssetRef>(CropTooSmall);
            }

            using var cropped = new SKBitmap(cropRect.Width, cropRect.Height);
            var extracted = bitmap.ExtractSubset(cropped, cropRect);
            if (!extracted)
            {
                return Result.Failure<StoredAssetRef>(DecodeFailed);
            }

            using var croppedImage = SKImage.FromBitmap(cropped);
            using var encoded = croppedImage.Encode(SKEncodedImageFormat.Png, 100);
            var content = new StoredAssetContent(encoded.ToArray(), "image/png");

            StoredAssetRef storedRef;
            try
            {
                storedRef = await _storage.PutAssetAsync(targetKey, content, ct);
            }
            catch (GeneratedAssetStorageException)
            {
                return Result.Failure<StoredAssetRef>(StoreFailed);
            }

            return Result.Success(storedRef);
        }
        catch (Exception)
        {
            // Contract: NEVER throws — any unexpected decode/crop failure degrades the caller
            // to the PositionHint+ColorHex chip instead of failing the generation job.
            return Result.Failure<StoredAssetRef>(Unexpected);
        }
    }

    /// <summary>
    /// Scales a normalised 0..1 bounding box to an integer pixel rect within
    /// <paramref name="imageWidth"/> x <paramref name="imageHeight"/>, clamped to the image bounds.
    /// </summary>
    private static SKRectI ScaleToPixelRect(BrandMarkBoundingBox box, int imageWidth, int imageHeight)
    {
        var left = (int)Math.Round(box.X * imageWidth, MidpointRounding.AwayFromZero);
        var top = (int)Math.Round(box.Y * imageHeight, MidpointRounding.AwayFromZero);
        var right = (int)Math.Round((box.X + box.Width) * imageWidth, MidpointRounding.AwayFromZero);
        var bottom = (int)Math.Round((box.Y + box.Height) * imageHeight, MidpointRounding.AwayFromZero);

        left = Math.Clamp(left, 0, imageWidth);
        top = Math.Clamp(top, 0, imageHeight);
        right = Math.Clamp(right, 0, imageWidth);
        bottom = Math.Clamp(bottom, 0, imageHeight);

        return new SKRectI(left, top, Math.Max(left, right), Math.Max(top, bottom));
    }
}
