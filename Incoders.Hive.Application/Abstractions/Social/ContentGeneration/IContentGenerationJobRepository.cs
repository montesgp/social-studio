namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Port for persisting and querying content generation jobs.
/// Implemented by EF Infrastructure; not visible to Domain.
/// </summary>
public interface IContentGenerationJobRepository
{
    Task InsertAsync(ContentGenerationJob job, CancellationToken ct);

    Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct);

    /// <summary>
    /// Loads a job by id without tenant filtering.
    /// FOR INTERNAL USE ONLY — called by the background worker after dequeuing from the internal channel.
    /// Never expose this over an HTTP boundary.
    /// </summary>
    Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct);

    Task UpdateStatusAsync(Guid id, string status, string? statusReason, string? errorCode, CancellationToken ct);

    Task UpdateManifestAsync(Guid id, string manifestJson, DateTime completedAt, CancellationToken ct);

    Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct);

    /// <summary>
    /// Returns the most-recently created completed/partial job for the given analysis + client.
    /// Returns null when no such job exists or the org does not match (tenant-scoped).
    /// Used by the regenerate command to recover the AI-generated payload for a specific format.
    /// </summary>
    Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(
        Guid analysisId,
        Guid organizationId,
        Guid clientId,
        CancellationToken ct);
}

/// <summary>
/// Lightweight application-layer projection of a content generation job.
/// Not a domain entity — owned by Application and mapped from EF data.
/// </summary>
public sealed record ContentGenerationJob(
    Guid Id,
    Guid OrganizationId,
    Guid? ClientId,
    Guid AnalysisId,
    IReadOnlyList<string> RequestedFormats,
    string Status,
    string? StatusReason,
    string? ErrorCode,
    string BrandSnapshotJson,
    string? ManifestJson,
    Guid CorrelationId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? RegenerationObjection = null);
