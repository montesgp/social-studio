namespace Incoders.Hive.Domain.Social.ContentStudio;

/// <summary>
/// Editorial lifecycle of a <see cref="ContentItem"/>. Distinct from the audit-only
/// content-plan Provenance status and from operational generation/analysis statuses.
/// Transitions are guarded by <see cref="ReviewStateTransitions"/>. <see cref="HandedOff"/> is terminal.
/// </summary>
public enum ReviewState
{
    Draft = 0,
    InReview = 1,
    Edited = 2,
    Approved = 3,
    Rejected = 4,
    Discarded = 5,
    ReadyForPublication = 6,
    HandedOff = 7,
}
