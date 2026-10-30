namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.content_generation_batches</c>.
///
/// One row per operator cart. The pieces themselves live in
/// <c>content_generation_jobs</c>, one job per piece, linked back through
/// <see cref="ContentGenerationJobData.BatchId"/>.
///
/// There are deliberately NO progress counter columns here: progress is counted from the child jobs
/// on read. A counter maintained by a worker that can die mid-batch would eventually lie.
/// </summary>
public sealed class ContentGenerationBatchData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? ClientId { get; set; }

    public Guid AnalysisId { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>The requested cart as jsonb: [{ "formatKey": "reel-script", "count": 2 }, …].</summary>
    public string CompositionJson { get; set; } = "[]";

    /// <summary>
    /// Denormalised total. Not a progress counter — it is the immutable size of the order, written
    /// once at creation, and it is what the UI divides by.
    /// </summary>
    public int TotalPieces { get; set; }

    public Guid CorrelationId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
