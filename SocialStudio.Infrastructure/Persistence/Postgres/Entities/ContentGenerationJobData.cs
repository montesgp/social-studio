namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.content_generation_jobs</c>.
/// Tracks the lifecycle of a content-package generation request.
/// </summary>
public sealed class ContentGenerationJobData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? ClientId { get; set; }

    public Guid AnalysisId { get; set; }

    /// <summary>
    /// Owning batch, when this job is one piece of a multi-piece cart. NULL for the original
    /// single-package path, which keeps working untouched — that is why this is nullable rather
    /// than a required back-reference.
    /// </summary>
    public Guid? BatchId { get; set; }

    /// <summary>Zero-based position of this piece within its batch. NULL outside a batch.</summary>
    public int? BatchPieceIndex { get; set; }

    public List<string> RequestedFormats { get; set; } = new();

    public string Status { get; set; } = string.Empty;

    public string? StatusReason { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorCode { get; set; }

    /// <summary>Frozen brand snapshot at job-start time (jsonb).</summary>
    public string BrandSnapshotJson { get; set; } = string.Empty;

    /// <summary>Package manifest once the job completes (jsonb, nullable).</summary>
    public string? ManifestJson { get; set; }

    public string? RegenerationObjection { get; set; }

    public Guid CorrelationId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<ContentGenerationFormatResultData> FormatResults { get; set; } = new List<ContentGenerationFormatResultData>();
    public GeneratedPackageData? Package { get; set; }
}
