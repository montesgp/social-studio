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

    Task<IReadOnlyList<ContentGenerationFormatResult>> ListByIdsAsync(
        IReadOnlySet<Guid> resultIds,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>([]);

    /// <summary>
    /// Returns every format result a client generated from a single analysis, across all jobs.
    /// Used by the analysis-scoped Content Studio materializer. Default returns empty for test doubles.
    /// </summary>
    Task<IReadOnlyList<ContentGenerationFormatResult>> ListByAnalysisAndClientAsync(
        Guid organizationId,
        Guid analysisId,
        Guid clientId,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>([]);

    /// <summary>
    /// Physically removes a format result row (and its asset children via FK cascade).
    /// Used when the last asset of a format has been deleted, so the empty folder disappears.
    /// Default no-op keeps lightweight test doubles compiling.
    /// </summary>
    Task DeleteByIdAsync(Guid formatResultId, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Replaces the composition spec JSON for a format result. Used to tombstone a deleted
    /// carousel slide in the spec without renumbering the remaining slides.
    /// Default no-op keeps lightweight test doubles compiling.
    /// </summary>
    Task UpdateCompositionSpecAsync(Guid formatResultId, string? compositionSpecJson, CancellationToken ct) =>
        Task.CompletedTask;
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
    DateTime UpdatedAt,
    // Batch the owning job belongs to, when it is one piece of a multi-piece cart.
    //
    // The studio needs this to tell SIBLINGS from REVISIONS: two results with the same format key are
    // the same piece regenerated when there is no batch, but two distinct pieces when there is.
    // Optional/last so existing positional constructions keep compiling.
    Guid? BatchId = null);
