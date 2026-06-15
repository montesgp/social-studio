namespace Incoders.Hive.Domain.Social.ContentStudio;

/// <summary>
/// Encodes the allowed <see cref="ReviewState"/> transition table for a <see cref="ContentItem"/>.
/// Pure static logic — no I/O, no EF references. Mirrors the ProfileAnalysisStatusTransitions pattern.
/// </summary>
public static class ReviewStateTransitions
{
    private static readonly HashSet<(ReviewState From, ReviewState To)> AllowedTransitions =
    [
        // From Draft (a generated item can be approved/rejected directly, or opened for review)
        (ReviewState.Draft, ReviewState.InReview),
        (ReviewState.Draft, ReviewState.Edited),
        (ReviewState.Draft, ReviewState.Approved),
        (ReviewState.Draft, ReviewState.Rejected),
        (ReviewState.Draft, ReviewState.Discarded),

        // From InReview
        (ReviewState.InReview, ReviewState.Edited),
        (ReviewState.InReview, ReviewState.Approved),
        (ReviewState.InReview, ReviewState.Rejected),
        (ReviewState.InReview, ReviewState.Discarded),

        // From Edited (Edited -> Edited allows continued edits)
        (ReviewState.Edited, ReviewState.InReview),
        (ReviewState.Edited, ReviewState.Edited),
        (ReviewState.Edited, ReviewState.Approved),
        (ReviewState.Edited, ReviewState.Rejected),
        (ReviewState.Edited, ReviewState.Discarded),

        // From Approved (re-open or stage; never directly published)
        (ReviewState.Approved, ReviewState.ReadyForPublication),
        (ReviewState.Approved, ReviewState.Edited),
        (ReviewState.Approved, ReviewState.Rejected),

        // From Rejected (reviewer re-engages)
        (ReviewState.Rejected, ReviewState.InReview),
        (ReviewState.Rejected, ReviewState.Edited),
        (ReviewState.Rejected, ReviewState.Discarded),

        // From Discarded (recover)
        (ReviewState.Discarded, ReviewState.InReview),

        // From ReadyForPublication (hand off, or un-stage back to Approved)
        (ReviewState.ReadyForPublication, ReviewState.HandedOff),
        (ReviewState.ReadyForPublication, ReviewState.Approved),
    ];

    /// <summary>Returns <c>true</c> when transitioning from <paramref name="from"/> to
    /// <paramref name="to"/> is permitted by the domain model.</summary>
    public static bool IsValid(ReviewState from, ReviewState to) =>
        AllowedTransitions.Contains((from, to));

    /// <summary>Returns <c>true</c> for terminal states (<see cref="ReviewState.HandedOff"/>).</summary>
    public static bool IsTerminal(ReviewState state) =>
        state is ReviewState.HandedOff;
}
