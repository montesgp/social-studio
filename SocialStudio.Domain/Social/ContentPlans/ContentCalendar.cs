namespace SocialStudio.Domain.Social.ContentPlans;

/// <summary>
/// Value Object representing the Content Calendar node of a Content Plan.
/// Holds an ordered, immutable list of calendar slots and its own Provenance.
/// </summary>
public sealed record ContentCalendar(
    IReadOnlyList<CalendarSlot> Slots,
    Provenance Provenance);
