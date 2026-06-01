namespace Incoders.Hive.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of the content plan period (year, month, kind), captured at job-start time.
/// Mirrors the period triple stored on ContentPlanData.
/// </summary>
/// <param name="Year">Four-digit year of the period.</param>
/// <param name="Month">Month number (1–12) of the period.</param>
/// <param name="PeriodKind">
/// Period kind as a short integer (e.g. 0 = Monthly).
/// Matches the PeriodKind column on the ContentPlan entity.
/// </param>
public sealed record PeriodSnapshot(
    int Year,
    int Month,
    short PeriodKind);
