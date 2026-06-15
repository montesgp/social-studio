namespace Incoders.Hive.Domain.Social.ContentStudio;

/// <summary>
/// Optional soft association from a content item to the content-plan calendar slot it fulfills.
/// All references are plain Guids / value mirrors (no EF navigation, no cross-context types).
/// Null in Phase 1; populated by the plan-linkage feature (Phase 4).
/// </summary>
public sealed record PlanContext(
    Guid ContentPlanId,
    PeriodRef Period,
    Guid CalendarSlotId);
