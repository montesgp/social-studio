using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Batches;
using Incoders.Hive.Application.Features.Social.Workflow;
using Incoders.Hive.Application.Features.Social.Workflow.Enqueue;
using Incoders.Hive.Domain.Social.Workflow;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Social.Workflow.Enqueue;

/// <summary>
/// RED tests for <see cref="EnqueueContentWorkflowCandidatesCommandHandler"/>. The handler turns N
/// operator-composed candidates into N independent queue items; a rejected candidate never blocks its
/// siblings (D4).
/// </summary>
public sealed class EnqueueContentWorkflowCandidatesCommandHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private const string ScriptKey = "reel-script";
    private const string CarouselKey = "feed-carousel";

    [Fact]
    public async Task Handle_RejectsCandidateWithEmptyComposition()
    {
        var candidateId = Guid.NewGuid();
        var (handler, repo, queue) = Build(SessionWith(candidateId, "https://tiktok.com/@a/video/1"));

        var result = await handler.Handle(
            Command([new WorkflowCandidateComposition(candidateId, [])]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Candidates.Single().Accepted);
        Assert.Empty(queue.Enqueued);
        Assert.Empty(repo.CompositionsSet);
    }

    [Fact]
    public async Task Handle_RejectsUnknownFormat()
    {
        var candidateId = Guid.NewGuid();
        var (handler, repo, queue) = Build(SessionWith(candidateId, "https://tiktok.com/@a/video/1"));

        var result = await handler.Handle(
            Command([new WorkflowCandidateComposition(candidateId, [new BatchCompositionItem("carousel", 1)])]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Candidates.Single().Accepted);
        Assert.Empty(queue.Enqueued);
        Assert.Empty(repo.CompositionsSet);
    }

    [Fact]
    public async Task Handle_RejectsCandidateWithoutUsableUrl()
    {
        var candidateId = Guid.NewGuid();
        // Empty external result id mirrors an ai-ideas-origin candidate: nothing to analyze.
        var (handler, repo, queue) = Build(SessionWith(candidateId, string.Empty));

        var result = await handler.Handle(
            Command([new WorkflowCandidateComposition(candidateId, [new BatchCompositionItem(ScriptKey, 1)])]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Candidates.Single().Accepted);
        Assert.Equal(WorkflowCandidateErrors.NotAnalyzable.Code, result.Value.Candidates.Single().RejectionReason);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task Handle_AcceptedCandidate_TransitionsStatusFromDiscoveredToQueued()
    {
        // Direct consequence of closing the discovered-vs-enqueued conflation bug: a genuinely accepted
        // composition must move the candidate out of the pre-enqueue `Discovered` status into `Queued`
        // so the workflow history read surface can tell it apart from an untouched discovery row.
        var candidateId = Guid.NewGuid();
        var (handler, repo, _) = Build(SessionWith(candidateId, "https://tiktok.com/@a/video/1"));

        var result = await handler.Handle(
            Command([new WorkflowCandidateComposition(candidateId, [new BatchCompositionItem(ScriptKey, 1)])]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Candidates.Single().Accepted);
        Assert.Contains(
            repo.StatusUpdates,
            update => update.CandidateId == candidateId && update.Status == WorkflowCandidateStatus.Queued);
    }

    [Fact]
    public async Task Handle_NCandidatesProduceNQueueItems()
    {
        var candidateA = Guid.NewGuid();
        var candidateB = Guid.NewGuid();
        var candidateC = Guid.NewGuid();
        var session = SessionWith(
            (candidateA, "https://tiktok.com/1"),
            (candidateB, "https://tiktok.com/2"),
            (candidateC, "https://tiktok.com/3"));
        var (handler, _, queue) = Build(session);

        var result = await handler.Handle(
            Command([
                new WorkflowCandidateComposition(candidateA, [new BatchCompositionItem(ScriptKey, 1)]),
                new WorkflowCandidateComposition(candidateB, [new BatchCompositionItem(CarouselKey, 2)]),
                new WorkflowCandidateComposition(candidateC, [new BatchCompositionItem(ScriptKey, 1), new BatchCompositionItem(CarouselKey, 1)]),
            ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, queue.Enqueued.Count);
        Assert.All(result.Value.Candidates, c => Assert.True(c.Accepted));
        // Sibling independence: three distinct queue items, none referencing another candidate.
        Assert.Equal(3, queue.Enqueued.Select(i => i.CandidateId).Distinct().Count());
    }

    [Fact]
    public async Task Handle_ReEnqueueingTheSameCompositionIsIdempotent()
    {
        var candidateId = Guid.NewGuid();
        var (handler, _, queue) = Build(SessionWith(candidateId, "https://tiktok.com/@a/video/1"));

        var composition = new WorkflowCandidateComposition(candidateId, [new BatchCompositionItem(ScriptKey, 1)]);

        var first = await handler.Handle(Command([composition]), CancellationToken.None);
        var second = await handler.Handle(Command([composition]), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value.Candidates.Single().Accepted);
        // Same composition re-submitted while non-terminal must not double-enqueue.
        Assert.Single(queue.Enqueued);
    }

    [Fact]
    public async Task Handle_ReturnsEstimatedMinutesFromTotalPieces()
    {
        var candidateId = Guid.NewGuid();
        var (handler, _, _) = Build(SessionWith(candidateId, "https://tiktok.com/@a/video/1"));

        var result = await handler.Handle(
            Command([new WorkflowCandidateComposition(candidateId, [new BatchCompositionItem(ScriptKey, 2)])]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // AnalysisMinutes(5) + PerPieceMinutes(2) * 2 pieces = 9.
        Assert.Equal(9, result.Value.EstimatedMinutes);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static EnqueueContentWorkflowCandidatesCommand Command(IReadOnlyList<WorkflowCandidateComposition> candidates) =>
        new(OrgId, ClientId, UserId, WorkflowId, candidates);

    private static WorkflowSession SessionWith(Guid candidateId, string externalResultId) =>
        SessionWith((candidateId, externalResultId));

    private static WorkflowSession SessionWith(params (Guid CandidateId, string ExternalResultId)[] candidates)
    {
        var session = WorkflowSession.Create(WorkflowId);
        foreach (var (candidateId, externalResultId) in candidates)
        {
            session.AddCandidate(WorkflowCandidate.Create(candidateId, externalResultId));
        }

        return session;
    }

    private static (
        EnqueueContentWorkflowCandidatesCommandHandler Handler,
        FakeSessionRepository Repo,
        FakeEnqueueQueue Queue) Build(WorkflowSession session)
    {
        var repo = new FakeSessionRepository(session);
        var queue = new FakeEnqueueQueue();
        var clock = new TestClock(new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc));
        var etaOptions = new ContentWorkflowEtaOptions();

        var handler = new EnqueueContentWorkflowCandidatesCommandHandler(repo, queue, clock, etaOptions);
        return (handler, repo, queue);
    }

    private sealed class FakeSessionRepository(WorkflowSession session) : IContentWorkflowSessionRepository
    {
        public List<(Guid CandidateId, string CompositionJson, string EnqueueKey)> CompositionsSet { get; } = [];

        public List<(Guid CandidateId, WorkflowCandidateStatus Status)> StatusUpdates { get; } = [];

        public Task InsertAsync(
            WorkflowSession s, Guid organizationId, Guid clientId, string clientName, string status,
            string? configJson, CancellationToken ct) => Task.CompletedTask;

        public Task<WorkflowSession?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<WorkflowSession?>(session);

        public Task<WorkflowSession?> GetByIdInternalAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<WorkflowSession?>(session);

        public Task ReplaceSelectionAsync(Guid sessionId, IReadOnlyList<Guid> candidateIds, CancellationToken ct) =>
            Task.CompletedTask;

        public Task SetCandidateCompositionAsync(Guid candidateId, string compositionJson, string enqueueKey, CancellationToken ct)
        {
            CompositionsSet.Add((candidateId, compositionJson, enqueueKey));
            return Task.CompletedTask;
        }

        public Task UpdateCandidateStatusAsync(
            Guid candidateId, WorkflowCandidateStatus status, Guid? analysisId, Guid? batchId, string? failureCode, CancellationToken ct)
        {
            StatusUpdates.Add((candidateId, status));
            return Task.CompletedTask;
        }

        public Task AppendEventAsync(WorkflowSessionEvent workflowEvent, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<WorkflowSessionSummary>> ListAsync(Guid organizationId, Guid? clientId, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorkflowSessionSummary>>([]);

        public Task AddCandidatesAsync(Guid sessionId, IReadOnlyList<WorkflowDiscoveredCandidate> candidates, CancellationToken ct) =>
            Task.CompletedTask;

        public Task UpdateSessionStateAsync(Guid sessionId, string status, string? configJson, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<WorkflowSessionDetail?> GetDetailAsync(Guid id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<WorkflowSessionDetail?>(null);

        public Task<WorkflowSessionDetail?> GetDetailInternalAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<WorkflowSessionDetail?>(null);
    }

    private sealed class FakeEnqueueQueue : IContentWorkflowEnqueueQueue
    {
        public List<ContentWorkflowEnqueueItem> Enqueued { get; } = [];

        public ValueTask EnqueueAsync(ContentWorkflowEnqueueItem item, CancellationToken ct = default)
        {
            Enqueued.Add(item);
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<ContentWorkflowEnqueueItem> DequeueAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
