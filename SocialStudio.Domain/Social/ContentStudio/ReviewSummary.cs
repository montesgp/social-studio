namespace SocialStudio.Domain.Social.ContentStudio;

/// <summary>
/// Roll-up of member review states for a <see cref="ContentSet"/>. Projected from item states
/// (an aggregation query in the read model), so the UI and bulk operations read one summary
/// instead of N item rows.
/// </summary>
public sealed record ReviewSummary(
    int Total,
    int Draft,
    int InReview,
    int Edited,
    int Approved,
    int Rejected,
    int Discarded,
    int ReadyForPublication,
    int HandedOff)
{
    public static readonly ReviewSummary Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>Builds a summary by counting the supplied member states.</summary>
    public static ReviewSummary From(IEnumerable<ReviewState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        var list = states as IReadOnlyCollection<ReviewState> ?? states.ToList();

        return new ReviewSummary(
            Total: list.Count,
            Draft: list.Count(s => s == ReviewState.Draft),
            InReview: list.Count(s => s == ReviewState.InReview),
            Edited: list.Count(s => s == ReviewState.Edited),
            Approved: list.Count(s => s == ReviewState.Approved),
            Rejected: list.Count(s => s == ReviewState.Rejected),
            Discarded: list.Count(s => s == ReviewState.Discarded),
            ReadyForPublication: list.Count(s => s == ReviewState.ReadyForPublication),
            HandedOff: list.Count(s => s == ReviewState.HandedOff));
    }
}
