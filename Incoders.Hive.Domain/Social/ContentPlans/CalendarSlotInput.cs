namespace Incoders.Hive.Domain.Social.ContentPlans;

/// <summary>
/// Input DTO carrying the data needed to add or replace a calendar slot.
/// Used by ContentPlan.ReplaceCalendarSlots to avoid exposing CalendarSlot mutation.
/// </summary>
public sealed record CalendarSlotInput(
    int Day,
    string ContentFormat,
    Provenance Provenance);
