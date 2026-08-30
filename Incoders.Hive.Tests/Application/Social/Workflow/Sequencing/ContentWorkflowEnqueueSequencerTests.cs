using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Batches;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;
using Incoders.Hive.Application.Features.Social.Workflow;
using Incoders.Hive.Application.Features.Social.Workflow.Sequencing;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Incoders.Hive.Domain.Social.Workflow;
using Incoders.Hive.Tests.Common;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.Workflow.Sequencing;

/// <summary>
/// RED tests for <see cref="ContentWorkflowEnqueueSequencer"/> (design "Analysis→batch sequencing",
/// decisions #3/#4). Uses a fake <see cref="ISender"/> + fake analysis repo + deterministic clock/delay
/// so the wait-timeout path never sleeps in real wall-clock time.
/// </summary>
public sealed class ContentWorkflowEnqueueSequencerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();

    [Fact]
    public async Task RunAsync_AnalysisThenBatch_RunsInOrderAndReachesBatchStarted()
    {
        var candidateId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var session = SessionWith(candidateId, "https://tiktok.com/@a/video/1", ("reel-script", 1));
        var repo = new FakeSessionRepository(session);
        var analyses = new FakeAnalysisRepository(analysisId, SingleContentAnalysisStatus.Analyzed);
        var sender = new FakeSender(analysisId, batchId);
        var clock = new TestClock(new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc));
        var delay = new FakeDelay(clock);

        var sequencer = Build(repo, analyses, sender, clock, delay);

        await sequencer.RunAsync(Item(candidateId), CancellationToken.None);

        Assert.Equal(
            [typeof(AnalyzeSingleContentCommand), typeof(CreateContentGenerationBatchCommand)],
            sender.SentTypes);
        Assert.Equal(
            [
                WorkflowCandidateStatus.Analyzing,
                WorkflowCandidateStatus.BatchQueued,
                WorkflowCandidateStatus.BatchStarted,
            ],
            repo.StatusHistory.Select(s => s.Status));
        Assert.Equal(batchId, repo.StatusHistory.Last().BatchId);
        Assert.Equal(analysisId, repo.StatusHistory.Last().AnalysisId);
    }

    [Fact]
    public async Task RunAsync_UsesCandidateSourceUrl_NotExternalResultId_ForAnalysis()
    {
        // BUG 1 regression: `ExternalResultId` is a discovery-time identity token (in production, the
        // candidate's own internal GUID reformatted — never a real URL). `AnalyzeSingleContentCommand`
        // must be called with the real persisted `SourceUrl`, not `ExternalResultId`.
        var candidateId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        const string externalResultId = "9f8b7c6d5e4f4a3b2c1d0e9f8a7b6c5d"; // GUID-shaped, NOT a URL
        const string sourceUrl = "https://tiktok.com/@a/video/1";

        var session = WorkflowSession.Create(SessionId);
        var candidate = WorkflowCandidate.Create(candidateId, externalResultId, sourceUrl);
        candidate.SetComposition([("reel-script", 1)]);
        session.AddCandidate(candidate);

        var repo = new FakeSessionRepository(session);
        var analyses = new FakeAnalysisRepository(analysisId, SingleContentAnalysisStatus.Analyzed);
        var sender = new FakeSender(analysisId, batchId);
        var clock = new TestClock(new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc));
        var delay = new FakeDelay(clock);

        var sequencer = Build(repo, analyses, sender, clock, delay);

        await sequencer.RunAsync(Item(candidateId), CancellationToken.None);

        Assert.Equal(sourceUrl, sender.LastAnalyzeUrl);
        Assert.NotEqual(externalResultId, sender.LastAnalyzeUrl);
        Assert.Equal(WorkflowCandidateStatus.BatchStarted, repo.StatusHistory.Last().Status);
    }

    [Fact]
    public async Task RunAsync_CandidateHasNoSourceUrl_FailsAnalysisWithoutCallingAnalyzer()
    {
        // Edge case: discovery persisted no usable URL (e.g. malformed payload). Must fail cleanly with
        // a specific failure code rather than sending an empty/invalid URL into the analyzer.
        var candidateId = Guid.NewGuid();

        var session = WorkflowSession.Create(SessionId);
        var candidate = WorkflowCandidate.Create(candidateId, Guid.NewGuid().ToString("N"), sourceUrl: null);
        candidate.SetComposition([("reel-script", 1)]);
        session.AddCandidate(candidate);

        var repo = new FakeSessionRepository(session);
        var analyses = new FakeAnalysisRepository(Guid.NewGuid(), SingleContentAnalysisStatus.Analyzed);
        var sender = new FakeSender(Guid.NewGuid(), Guid.NewGuid());
        var clock = new TestClock(new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc));
        var delay = new FakeDelay(clock);

        var sequencer = Build(repo, analyses, sender, clock, delay);

        await sequencer.RunAsync(Item(candidateId), CancellationToken.None);

        Assert.Empty(sender.SentTypes);
        Assert.Equal(WorkflowCandidateStatus.AnalysisFailed, repo.StatusHistory.Last().Status);
        Assert.Equal(WorkflowCandidateErrors.NotAnalyzable.Code, repo.StatusHistory.Last().FailureCode);
    }

    [Fact]
    public async Task RunAsync_AnalysisCommandFails_ReachesAnalysisFailed_AndNeverSendsBatchCommand()
    {
        var candidateId = Guid.NewGuid();
        var session = SessionWith(candidateId, "https://tiktok.com/@a/video/1", ("reel-script", 1));
        var repo = new FakeSessionRepository(session);
        var analyses = new FakeAnalysisRepository(Guid.NewGuid(), SingleContentAnalysisStatus.Analyzed);
        var sender = new FakeSender(Guid.Empty, Guid.Empty) { FailAnalyze = true };
        var clock = new TestClock(new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc));
        var delay = new FakeDelay(clock);

        var sequencer = Build(repo, analyses, sender, clock, delay);

        await sequencer.RunAsync(Item(candidateId), CancellationToken.None);

        Assert.DoesNotContain(typeof(CreateContentGenerationBatchCommand), sender.SentTypes);
        Assert.Equal(WorkflowCandidateStatus.AnalysisFailed, repo.StatusHistory.Last().Status);
    }

    [Fact]
    public async Task RunAsync_SiblingCandidates_OneFailsOneSucceeds_Independently()
    {
        var failingCandidateId = Guid.NewGuid();
        var succeedingCandidateId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var batchId = Guid.NewGuid();

        var failingSession = SessionWith(failingCandidateId, "https://tiktok.com/@a/video/1", ("reel-script", 1));
        var failingRepo = new FakeSessionRepository(failingSession);
        var failingSender = new FakeSender(Guid.Empty, Guid.Empty) { FailAnalyze = true };
        var clockA = new TestClock(new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc));
        var sequencerA = Build(failingRepo, new FakeAnalysisRepository(analysisId, SingleContentAnalysisStatus.Analyzed), failingSender, clockA, new FakeDelay(clockA));

        var succeedingSession = SessionWith(succeedingCandidateId, "https://tiktok.com/@b/video/2", ("reel-script", 1));
        var succeedingRepo = new FakeSessionRepository(succeedingSession);
        var succeedingSender = new FakeSender(analysisId, batchId);
        var clockB = new TestClock(new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc));
        var sequencerB = Build(succeedingRepo, new FakeAnalysisRepository(analysisId, SingleContentAnalysisStatus.Analyzed), succeedingSender, clockB, new FakeDelay(clockB));

        await sequencerA.RunAsync(Item(failingCandidateId, sessionId: failingSession.Id), CancellationToken.None);
        await sequencerB.RunAsync(Item(succeedingCandidateId, sessionId: succeedingSession.Id), CancellationToken.None);

        Assert.Equal(WorkflowCandidateStatus.AnalysisFailed, failingRepo.StatusHistory.Last().Status);
        Assert.Equal(WorkflowCandidateStatus.BatchStarted, succeedingRepo.StatusHistory.Last().Status);
    }

    [Fact]
    public async Task RunAsync_AnalysisNeverCompletes_TimesOutToAnalysisFailed_UsingConfiguredTimeout()
    {
        var candidateId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var session = SessionWith(candidateId, "https://tiktok.com/@a/video/1", ("reel-script", 1));
        var repo = new FakeSessionRepository(session);
        // Never reaches Analyzed/Failed — forces the bounded wait to exhaust the configured timeout.
        var analyses = new FakeAnalysisRepository(analysisId, SingleContentAnalysisStatus.Analyzing);
        var sender = new FakeSender(analysisId, Guid.NewGuid());
        var clock = new TestClock(new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc));
        var delay = new FakeDelay(clock);
        var options = new ContentWorkflowEnqueueOptions { AnalysisWaitTimeout = TimeSpan.FromMinutes(1) };

        var sequencer = new ContentWorkflowEnqueueSequencer(
            sender, repo, analyses, clock, delay, options, NullLogger<ContentWorkflowEnqueueSequencer>.Instance);

        await sequencer.RunAsync(Item(candidateId), CancellationToken.None);

        Assert.Equal(WorkflowCandidateStatus.AnalysisFailed, repo.StatusHistory.Last().Status);
        Assert.Equal(WorkflowCandidateErrors.AnalysisTimedOut.Code, repo.StatusHistory.Last().FailureCode);
        Assert.DoesNotContain(typeof(CreateContentGenerationBatchCommand), sender.SentTypes);
        // The fake delay advances the clock by PollInterval each call — proves the wait actually used
        // (at least) the configured budget rather than failing immediately or looping forever.
        Assert.True(clock.UtcNow - new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc) >= options.AnalysisWaitTimeout);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static ContentWorkflowEnqueueSequencer Build(
        FakeSessionRepository repo, FakeAnalysisRepository analyses, FakeSender sender, TestClock clock, FakeDelay delay) =>
        new(sender, repo, analyses, clock, delay, new ContentWorkflowEnqueueOptions(),
            NullLogger<ContentWorkflowEnqueueSequencer>.Instance);

    private static ContentWorkflowEnqueueItem Item(Guid candidateId, Guid? sessionId = null) =>
        new(sessionId ?? SessionId, candidateId, OrgId, ClientId, UserId);

    private static WorkflowSession SessionWith(Guid candidateId, string sourceUrl, (string FormatKey, int Count) composition)
    {
        var session = WorkflowSession.Create(SessionId);
        var candidate = WorkflowCandidate.Create(candidateId, Guid.NewGuid().ToString("N"), sourceUrl);
        candidate.SetComposition([composition]);
        session.AddCandidate(candidate);
        return session;
    }

    private sealed class FakeSessionRepository(WorkflowSession session) : IContentWorkflowSessionRepository
    {
        public List<(WorkflowCandidateStatus Status, Guid? AnalysisId, Guid? BatchId, string? FailureCode)> StatusHistory { get; } = [];

        public Task InsertAsync(
            WorkflowSession s, Guid organizationId, Guid clientId, string clientName, string status,
            string? configJson, CancellationToken ct) => Task.CompletedTask;

        public Task<WorkflowSession?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<WorkflowSession?>(session);

        public Task<WorkflowSession?> GetByIdInternalAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<WorkflowSession?>(session.Id == id ? session : null);

        public Task ReplaceSelectionAsync(Guid sessionId, IReadOnlyList<Guid> candidateIds, CancellationToken ct) =>
            Task.CompletedTask;

        public Task SetCandidateCompositionAsync(Guid candidateId, string compositionJson, string enqueueKey, CancellationToken ct) =>
            Task.CompletedTask;

        public Task UpdateCandidateStatusAsync(
            Guid candidateId, WorkflowCandidateStatus status, Guid? analysisId, Guid? batchId, string? failureCode, CancellationToken ct)
        {
            StatusHistory.Add((status, analysisId, batchId, failureCode));
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

    private sealed class FakeAnalysisRepository(Guid analysisId, SingleContentAnalysisStatus status) : ISingleContentAnalysisRepository
    {
        public Task AddAsync(SingleContentAnalysisRecord analysis, CancellationToken ct = default) => Task.CompletedTask;

        public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default) =>
            Task.FromResult<SingleContentAnalysisRecord?>(id == analysisId
                ? new SingleContentAnalysisRecord(
                    analysisId, organizationId, ClientId, UserId, "https://tiktok.com/@a/video/1", null, "tiktok",
                    status, null, null, null, status == SingleContentAnalysisStatus.Failed ? "boom" : null, null,
                    DateTime.UtcNow, DateTime.UtcNow, null)
                : null);

        public Task UpdateStatusAsync(
            Guid analysisId2, Guid organizationId, SingleContentAnalysisStatus s, DateTime updatedAtUtc,
            CancellationToken ct = default, string? failureCode = null, string? failureMessageKey = null) => Task.CompletedTask;

        public Task UpdateAsync(SingleContentAnalysisRecord analysis, CancellationToken ct = default) => Task.CompletedTask;

        public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
            Guid organizationId, Guid userId, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));

        public Task<IReadOnlyList<SingleContentAnalysisRecord>> ListCompletedByClientAsync(
            Guid organizationId, Guid clientId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SingleContentAnalysisRecord>>([]);

        public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
            Guid organizationId, Guid userId, string normalizedUrl, string sourceUrl, CancellationToken ct = default) =>
            Task.FromResult<SingleContentAnalysisHistoryItem?>(null);

        public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
            Guid analysisId2, Guid organizationId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>?>(null);
    }

    private sealed class FakeSender(Guid analysisId, Guid batchId) : ISender
    {
        public bool FailAnalyze { get; init; }

        public List<Type> SentTypes { get; } = [];

        public string? LastAnalyzeUrl { get; private set; }

        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            SentTypes.Add(request.GetType());

            if (request is AnalyzeSingleContentCommand analyzeCommand)
            {
                LastAnalyzeUrl = analyzeCommand.Url;
                object response = FailAnalyze
                    ? Result.Failure<SingleAnalyzerStatusResponse>(SingleAnalyzerErrors.AnalysisFailed)
                    : Result.Success(new SingleAnalyzerStatusResponse(
                        analysisId, OrgId, ClientId, "https://tiktok.com/@a/video/1", null, "tiktok",
                        SingleContentAnalysisStatus.Requested, null, null, null, null,
                        DateTime.UtcNow, DateTime.UtcNow, null));
                return Task.FromResult((TResponse)response);
            }

            if (request is CreateContentGenerationBatchCommand)
            {
                object response = Result.Success(new CreateContentGenerationBatchResponse(
                    batchId, 1, [], $"/api/social/content-generation-batches/{batchId}/progress"));
                return Task.FromResult((TResponse)response);
            }

            throw new NotSupportedException($"Unexpected request type: {request.GetType()}");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDelay(TestClock clock) : IAsyncDelay
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken ct = default)
        {
            clock.UtcNow = clock.UtcNow.Add(duration);
            return Task.CompletedTask;
        }
    }
}
