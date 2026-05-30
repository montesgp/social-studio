namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

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
