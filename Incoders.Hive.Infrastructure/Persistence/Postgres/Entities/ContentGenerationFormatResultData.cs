namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF data entity for <c>social.content_generation_format_results</c>.
/// One row per (job_id, format_key) — unique constraint enforced at DB level.
/// </summary>
public sealed class ContentGenerationFormatResultData
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public string FormatKey { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    /// <summary>AI-generated output payload (jsonb, nullable).</summary>
    public string? OutputJson { get; set; }

    /// <summary>Visual composition spec for this format (jsonb, nullable).</summary>
    public string? CompositionSpecJson { get; set; }

    /// <summary>URI to the rendered asset file (nullable until rendered).</summary>
    public string? RenderedAssetUri { get; set; }

    /// <summary>JSON list of inferred brand field keys (jsonb, nullable).</summary>
    public string? InferredFieldsJson { get; set; }

    public string? PromptVersion { get; set; }

    public string? ErrorCode { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ContentGenerationJobData? Job { get; set; }
}
