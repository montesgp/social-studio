namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for social.content_plans.
/// No annotations — all configuration done via fluent API in AppIdentityDbContext.
/// No navigation properties — sibling aggregate pattern.
/// Period is stored as three flat columns: period_kind, year, month.
/// Provenance for each node is stored as EF owned-type flat prefixed columns.
/// </summary>
public sealed class ContentPlanData
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public Guid OrganizationId { get; set; }

    // Period columns
    public short PeriodKind { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    // Objective node
    public string ObjectiveText { get; set; } = string.Empty;
    public List<string> ObjectiveKpis { get; set; } = new();

    // Objective Provenance (owned, prefixed objective_provenance_*)
    public short ObjectiveProvenanceSource { get; set; }
    public short ObjectiveProvenanceStatus { get; set; }
    public DateTime? ObjectiveProvenanceGeneratedAtUtc { get; set; }
    public string? ObjectiveProvenancePromptVersion { get; set; }

    // Content Strategy node
    public string? ContentStrategyText { get; set; }

    // Content Strategy Provenance (owned, prefixed content_strategy_provenance_*)
    public short ContentStrategyProvenanceSource { get; set; }
    public short ContentStrategyProvenanceStatus { get; set; }
    public DateTime? ContentStrategyProvenanceGeneratedAtUtc { get; set; }
    public string? ContentStrategyProvenancePromptVersion { get; set; }

    // Calendar-level Provenance (owned, prefixed calendar_provenance_*)
    public short CalendarProvenanceSource { get; set; }
    public short CalendarProvenanceStatus { get; set; }
    public DateTime? CalendarProvenanceGeneratedAtUtc { get; set; }
    public string? CalendarProvenancePromptVersion { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    // Slots are a separate table — no navigation property per sibling pattern
    public List<ContentPlanSlotData> Slots { get; set; } = new();
}
