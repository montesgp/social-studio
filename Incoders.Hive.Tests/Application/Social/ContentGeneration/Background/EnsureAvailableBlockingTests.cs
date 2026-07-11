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
/// TDD tests for <see cref="ClientAestheticEnsureFreshService.EnsureAvailableBlockingAsync"/> and
/// <see cref="ClientAestheticEnsureFreshService.LatestUsableAestheticPresentAsync"/> — the
/// first-time / no-usable-aesthetic blocking wait (issue #67, BAPA-CG-01/BAPA-CG-02, design
/// Decisions 2, 3, 6).
///
/// Uses a tiny poll interval (10ms) so the multi-poll scenarios run fast under CI.
/// </summary>
public sealed class EnsureAvailableBlockingTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid OwnAccountId = Guid.NewGuid();
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    [Fact]
    public async Task EnsureAvailableBlocking_NoOwnAccount_ReturnsImmediately_NoWaitNoEnqueue()
    {
        var analysisRepo = new FlippingAnalysisRepository(flipAfterPolls: 0, hasAestheticFromStart: false);
        var ownAccountRepo = new EmptyOwnAccountRepository();
        var jobQueue = new EnsureFreshServiceFreshnessTests.FakeJobQueue();
        var service = BuildService(analysisRepo, ownAccountRepo, jobQueue);

        var waitStartedCount = 0;
        var outcome = await service.EnsureAvailableBlockingAsync(
            OrgId, ClientId, TimeSpan.FromSeconds(5),
            onWaitStarted: _ => { waitStartedCount++; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal(EnsureAvailableOutcome.NoOwnAccount, outcome);
        Assert.Equal(0, waitStartedCount);
        Assert.Empty(jobQueue.Enqueued);
    }

    [Fact]
    public async Task EnsureAvailableBlocking_AestheticFlipsTrueAfterNPolls_ReturnsReady_OnWaitStartedFiredOnce()
    {
        var analysisRepo = new FlippingAnalysisRepository(flipAfterPolls: 3, hasAestheticFromStart: false);
        var ownAccountRepo = new SingleOwnAccountRepository(OwnAccountId);
        var jobQueue = new EnsureFreshServiceFreshnessTests.FakeJobQueue();
        var service = BuildService(analysisRepo, ownAccountRepo, jobQueue);

        var waitStartedCount = 0;
        var outcome = await service.EnsureAvailableBlockingAsync(
            OrgId, ClientId, TimeSpan.FromSeconds(5),
            onWaitStarted: _ => { waitStartedCount++; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal(EnsureAvailableOutcome.Ready, outcome);
        Assert.Equal(1, waitStartedCount);
        Assert.Single(jobQueue.Enqueued);
    }

    [Fact]
    public async Task EnsureAvailableBlocking_NoAestheticAndNoLongerInFlight_ReturnsAnalysisFailed()
    {
        var analysisRepo = new FlippingAnalysisRepository(flipAfterPolls: -1, hasAestheticFromStart: false, goesTerminalAfterPolls: 2);
        var ownAccountRepo = new SingleOwnAccountRepository(OwnAccountId);
        var jobQueue = new EnsureFreshServiceFreshnessTests.FakeJobQueue();
        var service = BuildService(analysisRepo, ownAccountRepo, jobQueue);

        var outcome = await service.EnsureAvailableBlockingAsync(
            OrgId, ClientId, TimeSpan.FromSeconds(5),
            onWaitStarted: null,
            CancellationToken.None);

        Assert.Equal(EnsureAvailableOutcome.AnalysisFailed, outcome);
    }

    [Fact]
    public async Task EnsureAvailableBlocking_NeverResolves_TinyMaxWait_ReturnsTimedOut_NoThrow()
    {
        // Stays no-aesthetic and in-flight forever — only the tiny maxWait ceiling can end the wait.
        var analysisRepo = new FlippingAnalysisRepository(flipAfterPolls: -1, hasAestheticFromStart: false);
        var ownAccountRepo = new SingleOwnAccountRepository(OwnAccountId);
        var jobQueue = new EnsureFreshServiceFreshnessTests.FakeJobQueue();
        var service = BuildService(analysisRepo, ownAccountRepo, jobQueue);

        var exception = await Record.ExceptionAsync(() => service.EnsureAvailableBlockingAsync(
            OrgId, ClientId, TimeSpan.FromMilliseconds(30),
            onWaitStarted: null,
            CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task EnsureAvailableBlocking_NeverResolves_TinyMaxWait_ReturnsTimedOutOutcome()
    {
        var analysisRepo = new FlippingAnalysisRepository(flipAfterPolls: -1, hasAestheticFromStart: false);
        var ownAccountRepo = new SingleOwnAccountRepository(OwnAccountId);
        var jobQueue = new EnsureFreshServiceFreshnessTests.FakeJobQueue();
        var service = BuildService(analysisRepo, ownAccountRepo, jobQueue);

        var outcome = await service.EnsureAvailableBlockingAsync(
            OrgId, ClientId, TimeSpan.FromMilliseconds(30),
            onWaitStarted: null,
            CancellationToken.None);

        Assert.Equal(EnsureAvailableOutcome.TimedOut, outcome);
    }

    [Fact]
    public async Task EnsureAvailableBlocking_JobCtCancelledMidWait_PropagatesOperationCanceledException()
    {
        var analysisRepo = new FlippingAnalysisRepository(flipAfterPolls: -1, hasAestheticFromStart: false);
        var ownAccountRepo = new SingleOwnAccountRepository(OwnAccountId);
        var jobQueue = new EnsureFreshServiceFreshnessTests.FakeJobQueue();
        var service = BuildService(analysisRepo, ownAccountRepo, jobQueue);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(20));

        // Task.Delay's cancellation surfaces as TaskCanceledException (a subclass of
        // OperationCanceledException) — use ThrowsAnyAsync so either the base or derived type
        // satisfies the "propagates a real cancellation" contract (Design Decision 6).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EnsureAvailableBlockingAsync(
            OrgId, ClientId, TimeSpan.FromSeconds(30), // maxWait long enough that only job ct fires first
            onWaitStarted: null,
            cts.Token));
    }

    // ── Reliability review follow-up: own-account resolution must be exception-safe ────────────
    // (issue #67) — the interface doc promises "never throws for non-cancellation reasons", but
    // step 1 (own-account resolution) originally had no try/catch, so a transient repo error
    // surfaced as an unhandled exception -> a generic "unhandled:{Type}" job failure instead of
    // the actionable aesthetic-analysis-failed code.

    [Fact]
    public async Task EnsureAvailableBlocking_OwnAccountRepoThrows_ReturnsAnalysisFailed_NoThrow()
    {
        var analysisRepo = new FlippingAnalysisRepository(flipAfterPolls: -1, hasAestheticFromStart: false);
        var ownAccountRepo = new ThrowingOwnAccountRepository();
        var jobQueue = new EnsureFreshServiceFreshnessTests.FakeJobQueue();
        var service = BuildService(analysisRepo, ownAccountRepo, jobQueue);

        var exception = await Record.ExceptionAsync(() => service.EnsureAvailableBlockingAsync(
            OrgId, ClientId, TimeSpan.FromSeconds(5),
            onWaitStarted: null,
            CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task EnsureAvailableBlocking_OwnAccountRepoThrows_OutcomeIsAnalysisFailed()
    {
        var analysisRepo = new FlippingAnalysisRepository(flipAfterPolls: -1, hasAestheticFromStart: false);
        var ownAccountRepo = new ThrowingOwnAccountRepository();
        var jobQueue = new EnsureFreshServiceFreshnessTests.FakeJobQueue();
        var service = BuildService(analysisRepo, ownAccountRepo, jobQueue);

        var outcome = await service.EnsureAvailableBlockingAsync(
            OrgId, ClientId, TimeSpan.FromSeconds(5),
            onWaitStarted: null,
            CancellationToken.None);

        Assert.Equal(EnsureAvailableOutcome.AnalysisFailed, outcome);
    }

    [Fact]
    public async Task EnsureAvailableBlocking_JobCtAlreadyCancelled_OwnAccountRepoNotReached_PropagatesCancellation()
    {
        // Real per-job cancellation must still win even during the own-account resolution step —
        // the exception-safety fix for the WARNING must not swallow OperationCanceledException.
        var analysisRepo = new FlippingAnalysisRepository(flipAfterPolls: -1, hasAestheticFromStart: false);
        var ownAccountRepo = new CancellationThrowingOwnAccountRepository();
        var jobQueue = new EnsureFreshServiceFreshnessTests.FakeJobQueue();
        var service = BuildService(analysisRepo, ownAccountRepo, jobQueue);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EnsureAvailableBlockingAsync(
            OrgId, ClientId, TimeSpan.FromSeconds(5),
            onWaitStarted: null,
            cts.Token));
    }

    [Fact]
    public async Task LatestUsableAestheticPresentAsync_DelegatesToRepository()
    {
        var presentRepo = new FlippingAnalysisRepository(flipAfterPolls: 0, hasAestheticFromStart: true);
        var absentRepo = new FlippingAnalysisRepository(flipAfterPolls: -1, hasAestheticFromStart: false);
        var ownAccountRepo = new SingleOwnAccountRepository(OwnAccountId);

        var presentService = BuildService(presentRepo, ownAccountRepo, new EnsureFreshServiceFreshnessTests.FakeJobQueue());
        var absentService = BuildService(absentRepo, ownAccountRepo, new EnsureFreshServiceFreshnessTests.FakeJobQueue());

        Assert.True(await presentService.LatestUsableAestheticPresentAsync(OrgId, ClientId, CancellationToken.None));
        Assert.False(await absentService.LatestUsableAestheticPresentAsync(OrgId, ClientId, CancellationToken.None));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClientAestheticEnsureFreshService BuildService(
        IProfileAnalysisRepository analysisRepo,
        IClientOwnAccountRepository ownAccountRepo,
        IProfileAnalysisJobQueue jobQueue)
    {
        var clock = new TestClock(Now);
        var clientRepo = new EnsureFreshServiceFreshnessTests.FakeClientRepository(null);
        var unitOfWork = new EnsureFreshServiceFreshnessTests.FakeUnitOfWork();

        return new ClientAestheticEnsureFreshService(
            analysisRepo,
            clientRepo,
            ownAccountRepo,
            unitOfWork,
            clock,
            jobQueue,
            NullLogger<ClientAestheticEnsureFreshService>.Instance,
            gatingOptions: new TestGatingOptions(PollInterval));
    }

    /// <summary>
    /// Minimal <c>IOptions&lt;T&gt;</c> wrapper forcing <c>PollIntervalSeconds = 0</c> (immediate
    /// <c>Task.Delay</c> yield between polls) so the multi-poll scenarios run fast under CI —
    /// config granularity is whole seconds; production defaults to 2s (see options class).
    /// </summary>
    private sealed class TestGatingOptions(TimeSpan _)
        : Microsoft.Extensions.Options.IOptions<
            Incoders.Hive.Application.Features.Social.ContentGeneration.Shared.ContentGenerationAestheticGatingOptions>
    {
        public Incoders.Hive.Application.Features.Social.ContentGeneration.Shared.ContentGenerationAestheticGatingOptions Value { get; } =
            new() { PollIntervalSeconds = 0 };
    }

    /// <summary>Simulates a transient repository failure while resolving the own account.</summary>
    private sealed class ThrowingOwnAccountRepository : IClientOwnAccountRepository
    {
        public Task<IReadOnlyList<ClientOwnAccount>> GetAllByClientAsync(ClientId clientId, Guid organizationId, CancellationToken ct) =>
            throw new InvalidOperationException("Simulated own-account repository failure.");
        public Task<ClientOwnAccount?> GetByIdAsync(ClientOwnAccountId id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<ClientOwnAccount?>(null);
        public Task<(IReadOnlyList<ClientOwnAccount> Items, int TotalCount)> ListByClientAsync(
            ClientId clientId, Guid organizationId, bool includeInactive, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<ClientOwnAccount>, int)>(([], 0));
        public Task<bool> ExistsByProfileUrlAsync(ClientId clientId, SocialNetworkId socialNetworkId, string profileUrl,
            ClientOwnAccountId? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(ClientOwnAccount ownAccount, CancellationToken ct) => Task.CompletedTask;
        public void Update(ClientOwnAccount account) { }
        public Task ReorderAsync(ClientId clientId, Guid organizationId, IReadOnlyList<Guid> orderedIds, CancellationToken ct) =>
            Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Simulates the real per-job <c>ct</c> firing during own-account resolution — throws the
    /// SAME <see cref="OperationCanceledException"/>-derived exception a cancelled repo call would
    /// naturally produce, honoring the passed-in token like a well-behaved adapter.
    /// </summary>
    private sealed class CancellationThrowingOwnAccountRepository : IClientOwnAccountRepository
    {
        public Task<IReadOnlyList<ClientOwnAccount>> GetAllByClientAsync(ClientId clientId, Guid organizationId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ClientOwnAccount>>([]);
        }
        public Task<ClientOwnAccount?> GetByIdAsync(ClientOwnAccountId id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<ClientOwnAccount?>(null);
        public Task<(IReadOnlyList<ClientOwnAccount> Items, int TotalCount)> ListByClientAsync(
            ClientId clientId, Guid organizationId, bool includeInactive, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<ClientOwnAccount>, int)>(([], 0));
        public Task<bool> ExistsByProfileUrlAsync(ClientId clientId, SocialNetworkId socialNetworkId, string profileUrl,
            ClientOwnAccountId? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(ClientOwnAccount ownAccount, CancellationToken ct) => Task.CompletedTask;
        public void Update(ClientOwnAccount account) { }
        public Task ReorderAsync(ClientId clientId, Guid organizationId, IReadOnlyList<Guid> orderedIds, CancellationToken ct) =>
            Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>No own account configured for the client.</summary>
    private sealed class EmptyOwnAccountRepository : IClientOwnAccountRepository
    {
        public Task<IReadOnlyList<ClientOwnAccount>> GetAllByClientAsync(ClientId clientId, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClientOwnAccount>>([]);
        public Task<ClientOwnAccount?> GetByIdAsync(ClientOwnAccountId id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<ClientOwnAccount?>(null);
        public Task<(IReadOnlyList<ClientOwnAccount> Items, int TotalCount)> ListByClientAsync(
            ClientId clientId, Guid organizationId, bool includeInactive, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<ClientOwnAccount>, int)>(([], 0));
        public Task<bool> ExistsByProfileUrlAsync(ClientId clientId, SocialNetworkId socialNetworkId, string profileUrl,
            ClientOwnAccountId? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(ClientOwnAccount ownAccount, CancellationToken ct) => Task.CompletedTask;
        public void Update(ClientOwnAccount account) { }
        public Task ReorderAsync(ClientId clientId, Guid organizationId, IReadOnlyList<Guid> orderedIds, CancellationToken ct) =>
            Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>A single active own account configured for the client.</summary>
    private sealed class SingleOwnAccountRepository(Guid ownAccountId) : IClientOwnAccountRepository
    {
        public Task<IReadOnlyList<ClientOwnAccount>> GetAllByClientAsync(ClientId clientId, Guid organizationId, CancellationToken ct)
        {
            var account = ClientOwnAccount.Restore(
                new ClientOwnAccountId(ownAccountId),
                clientId,
                organizationId,
                SocialNetworkId.New(),
                "https://www.instagram.com/testbrand/",
                handle: "testbrand",
                label: null,
                notes: null,
                isActive: true,
                createdAtUtc: DateTime.UtcNow,
                updatedAtUtc: DateTime.UtcNow);
            return Task.FromResult<IReadOnlyList<ClientOwnAccount>>([account]);
        }
        public Task<ClientOwnAccount?> GetByIdAsync(ClientOwnAccountId id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult<ClientOwnAccount?>(null);
        public Task<(IReadOnlyList<ClientOwnAccount> Items, int TotalCount)> ListByClientAsync(
            ClientId clientId, Guid organizationId, bool includeInactive, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<ClientOwnAccount>, int)>(([], 0));
        public Task<bool> ExistsByProfileUrlAsync(ClientId clientId, SocialNetworkId socialNetworkId, string profileUrl,
            ClientOwnAccountId? excludeId, CancellationToken ct) => Task.FromResult(false);
        public Task AddAsync(ClientOwnAccount ownAccount, CancellationToken ct) => Task.CompletedTask;
        public void Update(ClientOwnAccount account) { }
        public Task ReorderAsync(ClientId clientId, Guid organizationId, IReadOnlyList<Guid> orderedIds, CancellationToken ct) =>
            Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Analysis repository probe fake: controls the two poll-loop signals independently.
    /// - <paramref name="flipAfterPolls"/>: after this many calls to <c>LatestAnalyzedHasAestheticAsync</c>,
    ///   the aesthetic becomes present. -1 = never flips.
    /// - <paramref name="goesTerminalAfterPolls"/>: after this many calls to
    ///   <c>HasNonTerminalAnalysisForClientAsync</c>, it returns false (nothing in flight). null = always in flight.
    /// </summary>
    private sealed class FlippingAnalysisRepository(
        int flipAfterPolls,
        bool hasAestheticFromStart,
        int? goesTerminalAfterPolls = null) : IProfileAnalysisRepository
    {
        private int _hasAestheticCalls;
        private int _hasNonTerminalCalls;

        public Task AddAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DateTime?> GetLatestAnalyzedCompletedAtAsync(Guid orgId, Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DateTime?>(null);

        public Task<bool> HasNonTerminalAnalysisForClientAsync(Guid orgId, Guid clientId, CancellationToken cancellationToken = default)
        {
            _hasNonTerminalCalls++;

            // Call #1 happens in EnsureAvailableBlockingAsync step 2 (BEFORE the enqueue decision):
            // nothing is in flight yet, so it must return false to trigger the enqueue path.
            // From call #2 onward (inside the poll loop) the just-enqueued analysis IS in flight,
            // unless goesTerminalAfterPolls says it reached a terminal state.
            if (_hasNonTerminalCalls == 1)
            {
                return Task.FromResult(false);
            }

            var stillInFlight = goesTerminalAfterPolls is null || _hasNonTerminalCalls <= goesTerminalAfterPolls.Value + 1;
            return Task.FromResult(stillInFlight);
        }

        public Task<bool> LatestAnalyzedHasAestheticAsync(Guid orgId, Guid clientId, CancellationToken cancellationToken = default)
        {
            if (hasAestheticFromStart)
            {
                return Task.FromResult(true);
            }

            _hasAestheticCalls++;
            var flipped = flipAfterPolls >= 0 && _hasAestheticCalls > flipAfterPolls;
            return Task.FromResult(flipped);
        }

        public Task<DomainProfileAnalysis?> GetByIdAsync(Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DomainProfileAnalysis?>(null);

        public Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(Guid organizationId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileAnalysisHistoryPage([], 0, page, pageSize));

        public Task UpdateAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
