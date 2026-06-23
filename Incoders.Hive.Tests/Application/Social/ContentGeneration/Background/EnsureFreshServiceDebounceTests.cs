using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// TDD tests for ClientAestheticEnsureFreshService debounce logic (design §9 row 4).
/// Refs: CAG-CG-10, OQ-8; tasks 3.2.
/// </summary>
public sealed class EnsureFreshServiceDebounceTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId1 = Guid.NewGuid();
    private static readonly Guid ClientId2 = Guid.NewGuid();
    private static readonly Guid OwnAccountId = Guid.NewGuid();

    // ── Non-terminal analysis exists → SkippedInFlight, no enqueue ──────────

    [Fact]
    public async Task EnsureFresh_NonTerminalAnalysisExists_ReturnsSkippedInFlight()
    {
        // analysis is stale (45d old) but a non-terminal refresh is already in flight
        var latestAt = Now - TimeSpan.FromDays(45);
        var harness = EnsureFreshServiceFreshnessTests.BuildHarnessInternal(
            orgId: OrgId,
            clientId: ClientId1,
            ownAccountId: OwnAccountId,
            clock: new TestClock(Now),
            clientThreshold: null,
            latestAnalyzedAt: latestAt,
            hasNonTerminal: true,
            hasOwnAccount: true);

        var outcome = await harness.Service.EnsureFreshAsync(OrgId, ClientId1, CancellationToken.None);

        Assert.Equal(EnsureFreshOutcome.SkippedInFlight, outcome);
        Assert.Empty(harness.JobQueue.Enqueued);
    }

    [Fact]
    public async Task EnsureFresh_NonTerminalExists_EnqueueAsyncNotCalled()
    {
        // verifies EnqueueAsync is NOT called at all when debounced
        var harness = EnsureFreshServiceFreshnessTests.BuildHarnessInternal(
            orgId: OrgId,
            clientId: ClientId1,
            ownAccountId: OwnAccountId,
            clock: new TestClock(Now),
            clientThreshold: null,
            latestAnalyzedAt: null,        // missing → would normally enqueue
            hasNonTerminal: true,          // but there's one in-flight → debounce
            hasOwnAccount: true);

        var outcome = await harness.Service.EnsureFreshAsync(OrgId, ClientId1, CancellationToken.None);

        Assert.Equal(EnsureFreshOutcome.SkippedInFlight, outcome);
        Assert.Empty(harness.JobQueue.Enqueued);
    }

    [Fact]
    public async Task EnsureFresh_TwoClientsDebounceIndependently()
    {
        // C1 has in-flight → SkippedInFlight; C2 does not → RefreshEnqueued
        var staleAt = Now - TimeSpan.FromDays(45);

        var harnessC1 = EnsureFreshServiceFreshnessTests.BuildHarnessInternal(
            orgId: OrgId,
            clientId: ClientId1,
            ownAccountId: OwnAccountId,
            clock: new TestClock(Now),
            clientThreshold: null,
            latestAnalyzedAt: staleAt,
            hasNonTerminal: true,
            hasOwnAccount: true);

        var harnessC2 = EnsureFreshServiceFreshnessTests.BuildHarnessInternal(
            orgId: OrgId,
            clientId: ClientId2,
            ownAccountId: OwnAccountId,
            clock: new TestClock(Now),
            clientThreshold: null,
            latestAnalyzedAt: staleAt,
            hasNonTerminal: false,
            hasOwnAccount: true);

        var outcome1 = await harnessC1.Service.EnsureFreshAsync(OrgId, ClientId1, CancellationToken.None);
        var outcome2 = await harnessC2.Service.EnsureFreshAsync(OrgId, ClientId2, CancellationToken.None);

        Assert.Equal(EnsureFreshOutcome.SkippedInFlight, outcome1);
        Assert.Empty(harnessC1.JobQueue.Enqueued);

        Assert.Equal(EnsureFreshOutcome.RefreshEnqueued, outcome2);
        Assert.Single(harnessC2.JobQueue.Enqueued);
    }
}
