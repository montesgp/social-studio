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
    /// Returns all asset rows for a client's format results from a single analysis, across all jobs.
    /// Used by the analysis-scoped Content Studio materializer. Default returns empty for test doubles.
    /// </summary>
    Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisAndClientAsync(
        Guid organizationId,
        Guid analysisId,
        Guid clientId,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormatAssetRow>>([]);

    /// <summary>
    /// Physically removes all asset rows for the given format result.
    /// Used by delete-item (INC-5): storage objects must be deleted separately before calling this.
    /// </summary>
    Task DeleteByFormatResultIdAsync(Guid formatResultId, CancellationToken ct);

    /// <summary>
    /// Physically removes a single asset row by its id (one slide / one file).
    /// Storage object must be deleted separately before calling this.
    /// Default no-op keeps lightweight test doubles compiling.
    /// </summary>
    Task DeleteByIdAsync(Guid assetId, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Returns the number of asset rows still attached to a format result.
    /// Used to decide whether a format result (folder) is now empty and should be removed.
    /// Default returns 0 for test doubles.
    /// </summary>
    Task<int> CountByFormatResultIdAsync(Guid formatResultId, CancellationToken ct) =>
        Task.FromResult(0);
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
    DateTime UpdatedAt,
    DateTime JobCreatedAtUtc = default);
