namespace Incoders.Hive.Application.Abstractions.ContentAnalysis;

/// <summary>
/// Kind of work the background worker should perform on the job:
/// - Full: scrape via extraction provider + AI analysis (the original flow).
/// - RetryAi: re-use the already-persisted content snapshot and only re-run the AI step.
///   This avoids hitting the extraction provider (and burning tokens) when the snapshot is
///   already correct and only the AI stage failed previously.
/// </summary>
public enum SingleAnalysisJobKind
{
    Full = 0,
    RetryAi = 1,
}

/// <summary>
/// A queued single-analysis job. Carries only identifiers — the background worker reloads the
/// persisted record and runs the heavy work in its own scope, decoupled from the HTTP request.
/// </summary>
public sealed record SingleAnalysisJob(
    Guid AnalysisId,
    Guid OrganizationId,
    SingleAnalysisJobKind Kind = SingleAnalysisJobKind.Full);

/// <summary>
/// Bounded background queue for analyzer jobs. Producers (the POST endpoint) enqueue; the hosted
/// worker consumes. Keeps the long extraction + AI work off the request thread so a client
/// disconnect (RequestAborted) never cancels it, and caps concurrency under load.
/// </summary>
public interface ISingleAnalysisJobQueue
{
    ValueTask EnqueueAsync(SingleAnalysisJob job, CancellationToken cancellationToken = default);

    IAsyncEnumerable<SingleAnalysisJob> DequeueAllAsync(CancellationToken cancellationToken);
}
