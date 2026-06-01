namespace Incoders.Hive.Domain.Social.ContentPlans;

/// <summary>
/// Represents a single slot in the Content Calendar.
/// Each slot is one generation unit for a given day and format.
/// ProducedPackageId is a soft Guid reference to the Generated Content Package
/// produced from this slot — nullable until generation occurs.
/// </summary>
public sealed class CalendarSlot
{
    internal CalendarSlot(
        CalendarSlotId id,
        int day,
        string contentFormat,
        int sortOrder,
        Guid? producedPackageId,
        Provenance provenance)
    {
        Id = id;
        Day = day;
        ContentFormat = contentFormat;
        SortOrder = sortOrder;
        ProducedPackageId = producedPackageId;
        Provenance = provenance;
    }

    public CalendarSlotId Id { get; }

    /// <summary>Day of month (1–DaysInMonth for the period).</summary>
    public int Day { get; }

    /// <summary>Non-empty content format descriptor (e.g. "Reel", "Carousel").</summary>
    public string ContentFormat { get; }

    /// <summary>Explicit ordering index; preserved on retrieval.</summary>
    public int SortOrder { get; }

    /// <summary>
    /// Nullable Guid referencing the Generated Content Package produced from this slot.
    /// Soft reference only — no EF FK, no navigation. Null until generation occurs.
    /// </summary>
    public Guid? ProducedPackageId { get; internal set; }

    public Provenance Provenance { get; }
}
