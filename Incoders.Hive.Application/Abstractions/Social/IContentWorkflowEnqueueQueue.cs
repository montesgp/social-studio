namespace Incoders.Hive.Application.Abstractions.Social;

/// <summary>
/// One candidate's analysis-then-batch sequence to run. One queue item per candidate so sibling
/// failures are isolated by construction (fan-out, not a single item carrying N candidates).
/// </summary>
public sealed record ContentWorkflowEnqueueItem(
    Guid SessionId,
    Guid CandidateId,
    Guid OrganizationId,
    Guid ClientId,
    Guid UserId);

/// <summary>
/// Bounded background queue for workflow candidate enqueue items. Producers (the enqueue command
/// handler) enqueue; <c>ContentWorkflowEnqueueBackgroundWorker</c> consumes with bounded concurrency,
/// mirroring <c>ISingleAnalysisJobQueue</c>.
/// </summary>
public interface IContentWorkflowEnqueueQueue
{
    ValueTask EnqueueAsync(ContentWorkflowEnqueueItem item, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ContentWorkflowEnqueueItem> DequeueAllAsync(CancellationToken cancellationToken);
}
