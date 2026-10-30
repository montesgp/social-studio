namespace SocialStudio.Domain.Social.ContentGeneration;

/// <summary>
/// Immutable snapshot of the content calendar for a specific period, captured at job-start time.
/// Source is the ContentPlan.Slots collection.
/// </summary>
/// <param name="Slots">Calendar slots ordered by SortOrder ascending. Empty when no slots exist.</param>
public sealed record CalendarSnapshot(
    IReadOnlyList<CalendarSlotSnapshot> Slots);

/// <summary>
/// A single calendar slot within a period's content calendar.
/// </summary>
/// <param name="Day">Day of the month (1–31) on which the content is scheduled.</param>
/// <param name="ContentFormat">Format key (e.g. "caption", "carousel", "reel").</param>
/// <param name="SortOrder">Relative ordering within the calendar (ascending).</param>
public sealed record CalendarSlotSnapshot(
    int Day,
    string ContentFormat,
    int SortOrder);
