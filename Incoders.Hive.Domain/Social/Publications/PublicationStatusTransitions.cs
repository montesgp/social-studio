namespace Incoders.Hive.Domain.Social.Publications;

/// <summary>
/// Encodes the allowed <see cref="PublicationStatus"/> transition table for a <see cref="Publication"/>.
/// Pure static logic — no I/O, no EF references. Mirrors the <c>ReviewStateTransitions</c> pattern.
/// </summary>
public static class PublicationStatusTransitions
{
    private static readonly HashSet<(PublicationStatus From, PublicationStatus To)> AllowedTransitions =
    [
        // Due-fire (v1: manual/publish path; future dispatcher reuses the same transition)
        (PublicationStatus.Scheduled, PublicationStatus.Publishing),
        // Abandon before it runs
        (PublicationStatus.Scheduled, PublicationStatus.Cancelled),

        // Success
        (PublicationStatus.Publishing, PublicationStatus.Published),
        // Attempt failed (connector error) OR operator reclaim of a stuck Publishing row
        (PublicationStatus.Publishing, PublicationStatus.PublishFailed),

        // Manual retry (OQ-5)
        (PublicationStatus.PublishFailed, PublicationStatus.Publishing),
        // Give up
        (PublicationStatus.PublishFailed, PublicationStatus.Cancelled),
    ];

    /// <summary>Returns <c>true</c> when transitioning from <paramref name="from"/> to
    /// <paramref name="to"/> is permitted by the domain model.</summary>
    public static bool IsValid(PublicationStatus from, PublicationStatus to) =>
        AllowedTransitions.Contains((from, to));

    /// <summary>Returns <c>true</c> for terminal states (<see cref="PublicationStatus.Published"/>,
    /// <see cref="PublicationStatus.Cancelled"/>).</summary>
    public static bool IsTerminal(PublicationStatus status) =>
        status is PublicationStatus.Published or PublicationStatus.Cancelled;
}
