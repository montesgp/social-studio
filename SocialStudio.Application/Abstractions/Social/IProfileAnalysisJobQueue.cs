namespace SocialStudio.Application.Abstractions.Social;

/// <summary>
/// Kind of work the background processor should perform for a profile analysis job.
/// </summary>
public enum ProfileAnalysisJobKind
{
    /// <summary>Full pipeline: scrape + AI stages.</summary>
    Full = 0,

    /// <summary>
    /// AI-only retry: skips the scrape stage and re-runs only the AI stages using
    /// the persisted scrape data (<see cref="ProfileAnalysisJob.AnalysisId"/> must
    /// refer to a record already in <c>Analyzing</c> status with non-null
    /// ProfileSnapshotJson, RawPostsDatasetJson, and ServerDerivedStatsJson).
    /// </summary>
    RetryAi = 1,
}

/// <summary>
/// A queued profile analysis job. Carries only identifiers — the background worker reloads the
/// persisted record and runs the heavy work in its own scope, decoupled from the HTTP request.
/// Mirrors <see cref="ContentAnalysis.SingleAnalysisJob"/> for the profile-analysis pipeline.
/// </summary>
public sealed record ProfileAnalysisJob(
    Guid AnalysisId,
    Guid OrganizationId,
    ProfileAnalysisJobKind Kind = ProfileAnalysisJobKind.Full);

/// <summary>
/// Bounded background queue for profile-analysis jobs. Producers (the POST endpoint) enqueue;
/// the hosted background worker consumes. Keeps scrape + AI work off the request thread so a
/// client disconnect never cancels the run, and caps concurrency under load.
/// Implemented in Infrastructure (mirrors ISingleAnalysisJobQueue).
/// </summary>
public interface IProfileAnalysisJobQueue
{
    ValueTask EnqueueAsync(ProfileAnalysisJob job, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ProfileAnalysisJob> DequeueAllAsync(CancellationToken cancellationToken);
}
