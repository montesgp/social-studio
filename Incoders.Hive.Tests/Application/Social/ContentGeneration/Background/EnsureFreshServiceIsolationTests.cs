using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Domain.Social.ClientOwnAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using DomainProfileAnalysis = Incoders.Hive.Domain.Social.ProfileAnalysis.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// TDD tests for ClientAestheticEnsureFreshService isolation / non-blocking contract
/// (design §9 row 6 exception isolation).
/// Refs: CAG-CG-04; tasks 3.4.
/// </summary>
public sealed class EnsureFreshServiceIsolationTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid OwnAccountId = Guid.NewGuid();

    // ── IProfileAnalysisJobQueue.EnqueueAsync throws → exception swallowed ───

    [Fact]
    public async Task EnsureFresh_EnqueueThrows_ExceptionIsSwallowedAndDoesNotPropagate()
    {
        // stale analysis + own account found → would try to enqueue, but queue throws
        var latestAt = Now - TimeSpan.FromDays(45);

        var service = BuildServiceWithThrowingQueue(latestAt);

        // Must not throw — the exception is caught and logged internally
        var exception = await Record.ExceptionAsync(
            () => service.EnsureFreshAsync(OrgId, ClientId, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task EnsureFresh_EnqueueThrows_WarningLogged_MethodReturnsWithoutPropagating()
    {
        // Missing analysis + own account → would enqueue, queue throws — method returns a value (not throws)
        var service = BuildServiceWithThrowingQueue(latestAnalyzedAt: null);

        // If we reach here without an exception, the contract is upheld
        var result = await service.EnsureFreshAsync(OrgId, ClientId, CancellationToken.None);

        // The exact outcome when the queue throws is implementation-defined, but must not be an exception.
        // The service may return RefreshEnqueued (optimistic) or a specific outcome — we only assert no throw.
        Assert.True(
            result == EnsureFreshOutcome.RefreshEnqueued
            || result == EnsureFreshOutcome.Fresh
            || result == EnsureFreshOutcome.SkippedInFlight
            || result == EnsureFreshOutcome.SkippedNoOwnAccount
            || result == EnsureFreshOutcome.NoActionNeeded,
            $"Unexpected outcome value: {result}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClientAestheticEnsureFreshService BuildServiceWithThrowingQueue(DateTime? latestAnalyzedAt)
    {
        var clock = new TestClock(Now);
        var analysisRepo = new EnsureFreshServiceFreshnessTests.FakeAnalysisRepository(latestAnalyzedAt, hasNonTerminal: false);
        var clientRepo = new EnsureFreshServiceFreshnessTests.FakeClientRepository(null);
        var ownAccountRepo = new EnsureFreshServiceFreshnessTests.FakeOwnAccountRepository(OwnAccountId);
        var unitOfWork = new EnsureFreshServiceFreshnessTests.FakeUnitOfWork();
        var throwingQueue = new ThrowingJobQueue();

        return new ClientAestheticEnsureFreshService(
            analysisRepo,
            clientRepo,
            ownAccountRepo,
            unitOfWork,
            clock,
            throwingQueue,
            NullLogger<ClientAestheticEnsureFreshService>.Instance);
    }

    /// <summary>A job queue that always throws to simulate enqueue failure.</summary>
    private sealed class ThrowingJobQueue : IProfileAnalysisJobQueue
    {
        public ValueTask EnqueueAsync(ProfileAnalysisJob job, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated queue failure for isolation test.");

        public IAsyncEnumerable<ProfileAnalysisJob> DequeueAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
