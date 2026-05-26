namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Bounded background queue for content generation jobs.
/// Producers (the POST endpoint handler) enqueue a job id; the hosted background worker
/// dequeues and processes it in its own scope, decoupled from the HTTP request thread.
/// Matches the pattern established by <c>ISingleAnalysisJobQueue</c>.
/// </summary>
public interface IContentGenerationJobQueue
{
    /// <summary>Adds a job id to the queue. Awaits backpressure if the queue is full.</summary>
    Task EnqueueAsync(Guid jobId, CancellationToken ct);

    /// <summary>Returns an async stream of job ids as they become available.</summary>
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct);
}
