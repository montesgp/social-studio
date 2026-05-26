namespace Incoders.Hive.Application.Abstractions.ContentAnalysis;

/// <summary>
/// A queued single-analysis job. Carries only identifiers — the background worker reloads the
/// persisted record and runs the heavy work in its own scope, decoupled from the HTTP request.
/// </summary>
public sealed record SingleAnalysisJob(Guid AnalysisId, Guid OrganizationId);

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
