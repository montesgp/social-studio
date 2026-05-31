namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for storing and querying per-asset rows in
/// <c>social.content_generation_format_assets</c>.
/// </summary>
public interface IContentGenerationFormatAssetRepository
{
    /// <summary>
    /// Idempotently replaces all asset rows for a given format result.
    /// Implementation: delete existing rows for <paramref name="formatResultId"/> then insert fresh ones.
    /// </summary>
    Task UpsertManyForFormatAsync(
        Guid formatResultId,
        IReadOnlyList<FormatAssetRow> assets,
        CancellationToken ct);

    /// <summary>Returns all asset rows for all format results belonging to a given job.</summary>
    Task<IReadOnlyList<FormatAssetRow>> ListByJobAsync(Guid jobId, CancellationToken ct);

    /// <summary>
    /// Returns all asset rows for the given analysis scoped to an organization.
    /// Used to build the packages tree in INC-6 (not this batch, but the port is declared now).
    /// </summary>
    Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisClientAsync(
        Guid organizationId,
        Guid analysisId,
        CancellationToken ct);

    /// <summary>
    /// Physically removes all asset rows for the given format result.
    /// Used by delete-item (INC-5): storage objects must be deleted separately before calling this.
    /// </summary>
    Task DeleteByFormatResultIdAsync(Guid formatResultId, CancellationToken ct);
}

/// <summary>
/// Application-layer record for one stored asset.  Maps to one row in
/// <c>social.content_generation_format_assets</c>.
/// </summary>
public sealed record FormatAssetRow(
    Guid Id,
    Guid FormatResultId,
    int AssetIndex,
    string StoragePath,
    string Filename,
    string MimeType,
    long SizeBytes,
    string ContentHash,
    DateTime CreatedAt,
    DateTime UpdatedAt);
