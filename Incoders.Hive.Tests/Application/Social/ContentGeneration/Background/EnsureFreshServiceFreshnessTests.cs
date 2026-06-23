using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Social.ClientOwnAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using DomainProfileAnalysis = Incoders.Hive.Domain.Social.ProfileAnalysis.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Background;

/// <summary>
/// TDD tests for ClientAestheticEnsureFreshService freshness logic (design §9 row 3a).
/// Refs: CAG-CG-01, CAG-CG-03; tasks 3.1.
/// </summary>
public sealed class EnsureFreshServiceFreshnessTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid OwnAccountId = Guid.NewGuid();

    // ── null threshold → effective threshold = 30d ───────────────────────────

    [Fact]
    public async Task EnsureFresh_NullThresholdClientAnalysisExactly30DaysOld_ReturnsFresh()
    {
        // Client with null threshold → 30d default; analysis at exactly UtcNow - 30d → Fresh
        var latestAt = Now - TimeSpan.FromDays(30);
        var harness = BuildHarness(
            clientThreshold: null,
            latestAnalyzedAt: latestAt,
            hasNonTerminal: false,
            hasOwnAccount: true);

        var outcome = await harness.Service.EnsureFreshAsync(OrgId, ClientId, CancellationToken.None);

        Assert.Equal(EnsureFreshOutcome.Fresh, outcome);
        Assert.Empty(harness.JobQueue.Enqueued);
    }

    [Fact]
    public async Task EnsureFresh_NullThresholdAnalysisOneSecondBeyond30Days_ReturnsRefreshEnqueued()
    {
        // analysis at UtcNow - 30d - 1s → stale → RefreshEnqueued
        var latestAt = Now - TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1);
        var harness = BuildHarness(
            clientThreshold: null,
            latestAnalyzedAt: latestAt,
            hasNonTerminal: false,
            hasOwnAccount: true);

        var outcome = await harness.Service.EnsureFreshAsync(OrgId, ClientId, CancellationToken.None);

        Assert.Equal(EnsureFreshOutcome.RefreshEnqueued, outcome);
        Assert.Single(harness.JobQueue.Enqueued);
    }

    [Fact]
    public async Task EnsureFresh_CustomThreshold7d_AnalysisAt10d_ReturnsRefreshEnqueued()
    {
        // custom threshold 7d, analysis at UtcNow - 10d → stale (custom wins over 30d default)
        var latestAt = Now - TimeSpan.FromDays(10);
        var harness = BuildHarness(
            clientThreshold: 7,
            latestAnalyzedAt: latestAt,
            hasNonTerminal: false,
            hasOwnAccount: true);

        var outcome = await harness.Service.EnsureFreshAsync(OrgId, ClientId, CancellationToken.None);

        Assert.Equal(EnsureFreshOutcome.RefreshEnqueued, outcome);
        Assert.Single(harness.JobQueue.Enqueued);
    }

    [Fact]
    public async Task EnsureFresh_CustomThreshold60d_AnalysisExactly60DaysOld_ReturnsFresh()
    {
        // custom 60d, analysis at UtcNow - 60d → Fresh (at boundary, not stale)
        var latestAt = Now - TimeSpan.FromDays(60);
        var harness = BuildHarness(
            clientThreshold: 60,
            latestAnalyzedAt: latestAt,
            hasNonTerminal: false,
            hasOwnAccount: true);

        var outcome = await harness.Service.EnsureFreshAsync(OrgId, ClientId, CancellationToken.None);

        Assert.Equal(EnsureFreshOutcome.Fresh, outcome);
        Assert.Empty(harness.JobQueue.Enqueued);
    }

    [Fact]
    public async Task EnsureFresh_NoAnalysisAtAll_ReturnsRefreshEnqueued()
    {
        // missing analysis → enqueue
        var harness = BuildHarness(
            clientThreshold: null,
            latestAnalyzedAt: null,
            hasNonTerminal: false,
            hasOwnAccount: true);

        var outcome = await harness.Service.EnsureFreshAsync(OrgId, ClientId, CancellationToken.None);

        Assert.Equal(EnsureFreshOutcome.RefreshEnqueued, outcome);
        Assert.Single(harness.JobQueue.Enqueued);
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private static ServiceHarness BuildHarness(
        int? clientThreshold,
        DateTime? latestAnalyzedAt,
        bool hasNonTerminal,
        bool hasOwnAccount) =>
        BuildHarnessInternal(
            OrgId,
            ClientId,
            OwnAccountId,
            new TestClock(Now),
            clientThreshold,
            latestAnalyzedAt,
            hasNonTerminal,
            hasOwnAccount);

    /// <summary>
    /// Full-parameter builder shared with debounce and own-account test classes.
    /// </summary>
    public static ServiceHarness BuildHarnessInternal(
        Guid orgId,
        Guid clientId,
        Guid ownAccountId,
        IClock clock,
        int? clientThreshold,
        DateTime? latestAnalyzedAt,
        bool hasNonTerminal,
        bool hasOwnAccount)
    {
        var analysisRepo = new FakeAnalysisRepository(latestAnalyzedAt, hasNonTerminal);
        var clientRepo = new FakeClientRepository(clientThreshold);
        var ownAccountAccountRepo = new FakeOwnAccountRepository(hasOwnAccount ? ownAccountId : null);
        var unitOfWork = new FakeUnitOfWork();
        var jobQueue = new FakeJobQueue();

        var service = new ClientAestheticEnsureFreshService(
            analysisRepo,
            clientRepo,
            ownAccountAccountRepo,
            unitOfWork,
            clock,
            jobQueue,
            NullLogger<ClientAestheticEnsureFreshService>.Instance);

        return new ServiceHarness(service, jobQueue);
    }

    public sealed record ServiceHarness(
        ClientAestheticEnsureFreshService Service,
        FakeJobQueue JobQueue);

    // ── Fakes (public so sibling test classes can reuse them) ─────────────────

    public sealed class FakeAnalysisRepository : IProfileAnalysisRepository
    {
        private readonly DateTime? _latestAnalyzedAt;
        private readonly bool _hasNonTerminal;
        public List<DomainProfileAnalysis> Added { get; } = [];

        public FakeAnalysisRepository(DateTime? latestAnalyzedAt, bool hasNonTerminal)
        {
            _latestAnalyzedAt = latestAnalyzedAt;
            _hasNonTerminal = hasNonTerminal;
        }

        public Task AddAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default)
        {
            Added.Add(analysis);
            return Task.CompletedTask;
        }

        public Task<DateTime?> GetLatestAnalyzedCompletedAtAsync(Guid orgId, Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_latestAnalyzedAt);

        public Task<bool> HasNonTerminalAnalysisForClientAsync(Guid orgId, Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_hasNonTerminal);

        public Task<DomainProfileAnalysis?> GetByIdAsync(Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DomainProfileAnalysis?>(null);

        public Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(Guid organizationId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileAnalysisHistoryPage([], 0, page, pageSize));

        public Task UpdateAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    public sealed class FakeClientRepository : ISocialClientRepository
    {
        private readonly int? _threshold;

        public FakeClientRepository(int? threshold)
        {
            _threshold = threshold;
        }

        public Task<Client?> GetByIdAsync(ClientId id, Guid organizationId, CancellationToken cancellationToken = default)
        {
            var client = Client.Restore(
                id,
                organizationId,
                "Test Client",
                description: null,
                isActive: true,
                createdAtUtc: DateTime.UtcNow,
                updatedAtUtc: DateTime.UtcNow,
                sectorOrNiche: null,
                policy: null,
                aestheticRefreshThresholdDays: _threshold);
            return Task.FromResult<Client?>(client);
        }

        public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(
            SocialClientQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<Client>, int)>(([], 0));

        public Task AddAsync(Client client, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Update(Client client) { }
    }

    public sealed class FakeOwnAccountRepository : IClientOwnAccountRepository
    {
        private readonly Guid? _ownAccountId;

        public FakeOwnAccountRepository(Guid? ownAccountId)
        {
            _ownAccountId = ownAccountId;
        }

        public Task<IReadOnlyList<ClientOwnAccount>> GetAllByClientAsync(ClientId clientId, Guid organizationId, CancellationToken ct)
        {
            if (_ownAccountId is null)
                return Task.FromResult<IReadOnlyList<ClientOwnAccount>>([]);

            var account = ClientOwnAccount.Restore(
                new ClientOwnAccountId(_ownAccountId.Value),
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

    public sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    public sealed class FakeJobQueue : IProfileAnalysisJobQueue
    {
        public List<ProfileAnalysisJob> Enqueued { get; } = [];

        public ValueTask EnqueueAsync(ProfileAnalysisJob job, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(job);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<ProfileAnalysisJob> DequeueAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
