namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.generated_packages</c>.
/// 1:1 with <see cref="ContentGenerationJobData"/> via unique FK on job_id.
/// <para>
/// <c>ExpiresAt</c> is nullable and intentionally not used in v1 (ephemeral local storage).
/// Reserved for future retention policy (e.g. Google Drive adapter with TTL).
/// </para>
/// </summary>
public sealed class GeneratedPackageData
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public string StorageUri { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>JSON manifest embedded in the ZIP (jsonb).</summary>
    public string ManifestJson { get; set; } = string.Empty;

    /// <summary>
    /// Optional expiry timestamp. NOT used in v1 (ephemeral local storage, no TTL).
    /// Kept nullable for future retention policy without a schema migration.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation
    public ContentGenerationJobData? Job { get; set; }
}
