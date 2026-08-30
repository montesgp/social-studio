namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Port for persisting and querying content generation BATCHES — an operator's cart of N pieces.
///
/// A batch owns N jobs, one per piece. That split is forced by the layer below: a format result row
/// is keyed (JobId, FormatKey), so two carousels cannot live in one job. It also buys piece variety
/// for free, because the pipeline seeds its variation from the job id.
///
/// Implemented by EF Infrastructure; not visible to Domain.
/// </summary>
public interface IContentGenerationBatchRepository
{
    Task InsertAsync(ContentGenerationBatch batch, CancellationToken ct);

    /// <summary>Tenant-scoped: returns null for unknown or cross-tenant ids.</summary>
    Task<ContentGenerationBatch?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct);

    /// <summary>
    /// Derives live progress by counting the batch's child jobs. Tenant-scoped; null when the batch
    /// does not belong to the organization.
    /// </summary>
    Task<ContentGenerationBatchProgress?> GetProgressAsync(Guid id, Guid organizationId, CancellationToken ct);

    /// <summary>
    /// Batches that have not finished yet, for the tenant. Backs the global progress surface, which
    /// is why it is tenant-scoped rather than a global sweep.
    /// </summary>
    Task<IReadOnlyList<ContentGenerationBatch>> ListActiveAsync(Guid organizationId, CancellationToken ct);

    Task UpdateStatusAsync(Guid id, string status, DateTime? completedAt, CancellationToken ct);

    /// <summary>
    /// Recomputes the batch's status from its child jobs and persists it.
    ///
    /// Called after every piece finishes, so the stored status is a projection of the pieces rather
    /// than a counter someone has to keep correct. NOT tenant-filtered: the background worker calls
    /// it after dequeuing, where no tenant context exists. Never expose over HTTP.
    /// </summary>
    Task ReconcileStatusAsync(Guid batchId, CancellationToken ct);
}

/// <summary>One format and how many pieces of it the batch was asked to produce.</summary>
public sealed record ContentGenerationBatchEntry(string FormatKey, int Count);

/// <summary>
/// Application-layer projection of a batch. Not a domain entity — mapped from EF data.
/// </summary>
public sealed record ContentGenerationBatch(
    Guid Id,
    Guid OrganizationId,
    Guid? ClientId,
    Guid AnalysisId,
    string Status,
    IReadOnlyList<ContentGenerationBatchEntry> Composition,
    int TotalPieces,
    Guid CorrelationId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt);

/// <summary>
/// Live progress for a batch, DERIVED from its child jobs rather than stored.
///
/// Storing counters would mean keeping them in sync from a background worker that can crash
/// mid-piece; a stale "7 of 10" that never moves again is worse than no number at all. Counting is
/// cheap and cannot drift.
/// </summary>
public sealed record ContentGenerationBatchProgress(
    Guid BatchId,
    string Status,
    int TotalPieces,
    int CompletedPieces,
    int FailedPieces,
    int RunningPieces,
    int QueuedPieces,
    /// <summary>Zero-based index of the piece currently being generated, when one is running.</summary>
    int? CurrentPieceIndex,
    /// <summary>Format of the piece currently being generated, so the UI can name it.</summary>
    string? CurrentFormatKey);
