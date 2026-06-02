namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Port for persisting and querying per-format results for a generation job.
/// </summary>
public interface IContentGenerationFormatResultRepository
{
    /// <summary>
    /// Upserts the result for a (jobId, formatKey) pair and returns the persisted row.
    /// The returned Id is authoritative for child rows such as format assets.
    /// </summary>
    Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct);

    Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct);
}

/// <summary>
/// Application-layer projection of a single format result row.
/// </summary>
public sealed record ContentGenerationFormatResult(
    Guid Id,
    Guid JobId,
    string FormatKey,
    string Status,
    string? OutputJson,
    string? CompositionSpecJson,
    string? RenderedAssetUri,
    string? InferredFieldsJson,
    string? PromptVersion,
    string? ErrorCode,
    DateTime CreatedAt,
    DateTime UpdatedAt);
