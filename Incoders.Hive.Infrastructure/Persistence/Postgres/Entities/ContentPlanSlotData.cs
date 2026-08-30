namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for social.content_plan_slots.
/// No annotations — all configuration done via fluent API in AppIdentityDbContext.
/// FK to content_plans with cascade delete.
/// ProducedPackageId is a soft Guid — no FK to any package table.
/// Provenance owned as flat prefixed columns (slot_provenance_*).
/// </summary>
public sealed class ContentPlanSlotData
{
    public Guid Id { get; set; }

    public Guid ContentPlanId { get; set; }

    public int Day { get; set; }

    public string ContentFormat { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    /// <summary>
    /// Soft reference to a Generated Content Package. No EF FK — plain Guid or null.
    /// </summary>
    public Guid? ProducedPackageId { get; set; }

    // Slot Provenance (owned, prefixed slot_provenance_*)
    public short SlotProvenanceSource { get; set; }
    public short SlotProvenanceStatus { get; set; }
    public DateTime? SlotProvenanceGeneratedAtUtc { get; set; }
    public string? SlotProvenancePromptVersion { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    // Navigation back to plan — used only for EF cascade config; never traversed in queries
    public ContentPlanData ContentPlan { get; set; } = null!;
}
