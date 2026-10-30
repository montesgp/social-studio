namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.content_generation_format_assets</c>.
/// One row per rendered asset; carousel produces N rows (one per slide).
/// FK → <c>social.content_generation_format_results(id)</c> ON DELETE CASCADE.
/// </summary>
public sealed class ContentGenerationFormatAssetData
{
    public Guid Id { get; set; }

    /// <summary>FK → content_generation_format_results.id</summary>
    public Guid FormatResultId { get; set; }

    /// <summary>
    /// 0 for single-asset formats; 1-based slide index for carousel.
    /// UNIQUE constraint with (format_result_id, asset_index).
    /// </summary>
    public int AssetIndex { get; set; }

    /// <summary>Full storage path: {orgId}/{clientId}/{analysisId}/[carousel/]filename</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>File name as stored in the bucket, e.g. "story.png" or "slide_1.png".</summary>
    public string Filename { get; set; } = string.Empty;

    /// <summary>MIME type of the stored asset, e.g. "image/png" or "text/plain; charset=utf-8".</summary>
    public string MimeType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>SHA-256 hex of the asset bytes.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ContentGenerationFormatResultData? FormatResult { get; set; }
}
