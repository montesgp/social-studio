using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Background;
using SocialStudio.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// TDD tests for ClientAestheticEnsureFreshService own-account resolution and degrade
/// path (design §9 row 5, §4.1).
/// Refs: CAG-CG-02; tasks 3.3.
/// </summary>
public sealed class EnsureFreshServiceOwnAccountTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid OwnAccountId = Guid.NewGuid();

    // ── No own account → SkippedNoOwnAccount, no enqueue, no exception ───────

    [Fact]
    public async Task EnsureFresh_NoOwnAccount_ReturnsSkippedNoOwnAccount()
    {
        // analysis is stale so refresh would normally be enqueued — but no own account
        var latestAt = Now - TimeSpan.FromDays(45);
        var harness = EnsureFreshServiceFreshnessTests.BuildHarnessInternal(
            orgId: OrgId,
            clientId: ClientId,
            ownAccountId: OwnAccountId,
            clock: new TestClock(Now),
            clientThreshold: null,
            latestAnalyzedAt: latestAt,
            hasNonTerminal: false,
            hasOwnAccount: false);

        var outcome = await harness.Service.EnsureFreshAsync(OrgId, ClientId, CancellationToken.None);

        Assert.Equal(EnsureFreshOutcome.SkippedNoOwnAccount, outcome);
        Assert.Empty(harness.JobQueue.Enqueued);
    }

    [Fact]
    public async Task EnsureFresh_NoOwnAccount_DoesNotThrow()
    {
        // must not throw; generation continues
        var harness = EnsureFreshServiceFreshnessTests.BuildHarnessInternal(
            orgId: OrgId,
            clientId: ClientId,
            ownAccountId: OwnAccountId,
            clock: new TestClock(Now),
            clientThreshold: null,
            latestAnalyzedAt: null,     // no analysis either
            hasNonTerminal: false,
            hasOwnAccount: false);

        var exception = await Record.ExceptionAsync(
            () => harness.Service.EnsureFreshAsync(OrgId, ClientId, CancellationToken.None));

        Assert.Null(exception);
    }

    // ── Own account found and analysis stale → GetAllByClientAsync called, enqueue occurs

    [Fact]
    public async Task EnsureFresh_OwnAccountFoundAndAnalysisStale_ReturnsRefreshEnqueued()
    {
        var latestAt = Now - TimeSpan.FromDays(45);
        var harness = EnsureFreshServiceFreshnessTests.BuildHarnessInternal(
            orgId: OrgId,
            clientId: ClientId,
            ownAccountId: OwnAccountId,
            clock: new TestClock(Now),
            clientThreshold: null,
            latestAnalyzedAt: latestAt,
            hasNonTerminal: false,
            hasOwnAccount: true);

        var outcome = await harness.Service.EnsureFreshAsync(OrgId, ClientId, CancellationToken.None);

        Assert.Equal(EnsureFreshOutcome.RefreshEnqueued, outcome);
        Assert.Single(harness.JobQueue.Enqueued);
    }
}
