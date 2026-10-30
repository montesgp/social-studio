using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for extracting an element's crop (icon-type brand mark, decorative
/// ornament, or any future bounding-box-scoped element) from the archived source image and
/// persisting it as a durable asset (WU-7b, D13; generalised in D3). Crop-only: no matting, no
/// vision API call, no logo regeneration — it decodes bytes the pipeline itself already archived
/// and slices the region a validated bounding box already describes.
///
/// <para>
/// Precedent: <see cref="IImageGuardrailValidator"/>'s concrete Infrastructure implementation
/// (<c>DegenerateImageGuardrailValidator</c>) already performs SkiaSharp image analysis behind
/// an Application port — this is an extension of that established pattern, not a novel
/// architectural category (design.md F4/D13).
/// </para>
///
/// <para>
/// Contract: NEVER throws. Any failure — the source image cannot be opened, the bytes cannot be
/// decoded, the bounding box is invalid, the resulting crop is below the minimum pixel size, or
/// the crop cannot be persisted — returns <c>Result.Failure</c>. The caller (<c>BuildBrandMarkSlot</c>)
/// degrades to the pre-existing <c>PositionHint</c>+<c>ColorHex</c> chip treatment on failure; the
/// generation job itself is never failed by an extraction failure.
/// </para>
/// </summary>
public interface IAestheticAssetCropExtractor
{
    /// <summary>
    /// Crops an element (icon-type brand mark, decorative ornament, etc.) out of the archived
    /// <paramref name="sourceImagePath"/> using <paramref name="box"/> (normalised 0..1,
    /// top-left origin) and persists the crop via
    /// <see cref="IGeneratedAssetStorage"/> at <paramref name="targetKey"/>. Returns the stored
    /// asset reference on success; never throws.
    /// </summary>
    Task<Result<StoredAssetRef>> ExtractAsync(
        StoredAssetKey targetKey,
        string sourceImagePath,
        BrandMarkBoundingBox box,
        CancellationToken ct);
}
