namespace Incoders.Hive.Domain.Social.Publications;

/// <summary>
/// Lifecycle status of a single <see cref="Publication"/> (one publish action toward one destination).
/// Orthogonal to <c>Incoders.Hive.Domain.Social.ContentStudio.ReviewState</c> — never coupled to it.
/// </summary>
public enum PublicationStatus
{
    /// <summary>Queued for a future <c>ScheduledForUtc</c>; no infra fires it in v1.</summary>
    Scheduled = 0,

    /// <summary>An attempt is in flight against the connector.</summary>
    Publishing = 1,

    /// <summary>Provider confirmed; terminal (holds the external reference).</summary>
    Published = 2,

    /// <summary>Last attempt failed; retryable back to <see cref="Publishing"/>.</summary>
    PublishFailed = 3,

    /// <summary>Operator abandoned before <see cref="Published"/>; terminal.</summary>
    Cancelled = 4,
}
