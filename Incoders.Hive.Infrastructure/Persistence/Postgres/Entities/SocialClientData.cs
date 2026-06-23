namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class SocialClientData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? SectorOrNiche { get; set; }

    /// <summary>
    /// Nullable background sourcing policy for this client.
    /// Maps to the <c>background_sourcing_policy</c> <c>smallint NULL</c> column on
    /// <c>social.clients</c>. Stored as a <c>short?</c> via
    /// <c>HasConversion&lt;short?&gt;()</c> so provider types stay out of Domain.
    /// <c>NULL</c> == <c>Unset</c> == legacy 3-tier behavior (backward-compatible default).
    /// Refs: design §10.1; BSP-07.
    /// </summary>
    public short? BackgroundSourcingPolicy { get; set; }

    /// <summary>
    /// Per-client override for the aesthetic analysis staleness threshold (days).
    /// Maps to the <c>aesthetic_refresh_threshold_days</c> <c>smallint NULL</c> column on
    /// <c>social.clients</c>. Stored as a <c>short?</c> — value range 1–365 fits well within
    /// <c>smallint</c>. <c>NULL</c> means use the system default (30 days).
    /// Refs: design §2.3; CAG-CS-01.
    /// </summary>
    public short? AestheticRefreshThresholdDays { get; set; }

    /// <summary>
    /// Per-client number of profile posts analysed per run. Maps to <c>max_posts_to_analyze</c>
    /// <c>smallint NULL</c> on <c>social.clients</c>. NULL = system default (12). Range 1–50.
    /// </summary>
    public short? MaxPostsToAnalyze { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public TenantData? Organization { get; set; }
}
