namespace SocialStudio.Domain.Social.Publications;

/// <summary>
/// A single publish attempt against the connector. Append-only, ordered by <see cref="AttemptNumber"/>
/// (1-based, monotonically increasing). The owning <see cref="Publication"/> opens a new attempt on
/// each <c>BeginPublishing</c>/retry and closes it via <c>MarkPublished</c>/<c>MarkFailed</c>.
/// </summary>
public sealed record PublicationAttempt(
    int AttemptNumber,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    PublicationAttemptOutcome Outcome,
    string? ErrorMessage,
    string? ExternalReference);
