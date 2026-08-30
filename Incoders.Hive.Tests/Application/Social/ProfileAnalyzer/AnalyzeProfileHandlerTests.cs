using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientOwnAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Tests.Common;
using DomainProfileAnalysis = Incoders.Hive.Domain.Social.ProfileAnalysis.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ProfileAnalyzer;

/// <summary>
/// Unit tests for AnalyzeProfileHandler (PA-01, PA-02, PA-03).
/// Tests validate the command-dispatch stage (create record + enqueue).
/// IProfileScrapeResolver MUST NOT be called during the command-dispatch stage.
/// </summary>
public sealed class AnalyzeProfileHandlerTests
{
    private static readonly DateTime Now = new(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

    // ── PA-01: Client path with valid Instagram own account → Requested ───────

    [Fact]
    public async Task Handle_ClientPathWithValidOwnAccount_ReturnsRequestedStatus()
    {
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var ownAccountId = Guid.NewGuid();
        var harness = new HandlerHarness(Now);
        harness.OwnAccountRepo.AddAccount(ownAccountId, clientId, orgId, "https://instagram.com/testbrand");

        var result = await harness.Handler.Handle(
            new AnalyzeProfileCommand(
                orgId,
                Guid.NewGuid(),
                ClientId: clientId,
                OwnAccountId: ownAccountId,
                ProfileUrl: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Requested", result.Value.Status);
        Assert.Equal(orgId, result.Value.OrganizationId);
        Assert.Equal(clientId, result.Value.ClientId);
        Assert.Equal(ownAccountId, result.Value.OwnAccountId);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }

    // ── PA-03: Both sources supplied → 400 validation error ──────────────────

    [Fact]
    public async Task Handle_BothSourcesSupplied_ReturnsValidationError()
    {
        var harness = new HandlerHarness(Now);

        var result = await harness.Handler.Handle(
            new AnalyzeProfileCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ClientId: Guid.NewGuid(),
                OwnAccountId: Guid.NewGuid(),
                ProfileUrl: "https://instagram.com/someone"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.profile_source_mutually_exclusive", result.Error.Code);
    }

    // ── PA-03: Neither source supplied → 400 validation error ────────────────

    [Fact]
    public async Task Handle_NoSourceSupplied_ReturnsValidationError()
    {
        var harness = new HandlerHarness(Now);

        var result = await harness.Handler.Handle(
            new AnalyzeProfileCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ClientId: null,
                OwnAccountId: null,
                ProfileUrl: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.profile_source_required", result.Error.Code);
    }

    // ── PA-02: URL path → null ClientId/OwnAccountId in created record ────────

    [Fact]
    public async Task Handle_UrlPath_CreatesRecordWithNullClientAndOwnAccount()
    {
        var orgId = Guid.NewGuid();
        var harness = new HandlerHarness(Now);

        var result = await harness.Handler.Handle(
            new AnalyzeProfileCommand(
                orgId,
                Guid.NewGuid(),
                ClientId: null,
                OwnAccountId: null,
                ProfileUrl: "https://www.instagram.com/cristiano"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Requested", result.Value.Status);
        Assert.Null(result.Value.ClientId);
        Assert.Null(result.Value.OwnAccountId);
    }

    [Fact]
    public async Task Handle_UrlPathWithInstagramShareQuery_SanitizesStoredProfileUrl()
    {
        var orgId = Guid.NewGuid();
        var harness = new HandlerHarness(Now);

        var result = await harness.Handler.Handle(
            new AnalyzeProfileCommand(
                orgId,
                Guid.NewGuid(),
                ClientId: null,
                OwnAccountId: null,
                ProfileUrl: "https://www.instagram.com/demo.creator?utm_source=ig_web_button_share_sheet&igsh=ZDNlZDc0MzIxNw=="),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://www.instagram.com/demo.creator/", result.Value.ProfileUrlOrHandle);
        Assert.Equal("https://www.instagram.com/demo.creator/", harness.Repository.Stored!.ProfileUrl);
    }

    [Fact]
    public void Validator_ProfileUrlWithInstagramShareQuery_IsAccepted()
    {
        var validator = new AnalyzeProfileValidator();

        var result = validator.Validate(
            new AnalyzeProfileCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ClientId: null,
                OwnAccountId: null,
                ProfileUrl: "https://www.instagram.com/demo.creator?utm_source=ig_web_button_share_sheet&igsh=ZDNlZDc0MzIxNw=="));

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
    }

    // ── PA-01: Client path with no own Instagram account → validation error ───

    [Fact]
    public async Task Handle_ClientPathWithUnknownOwnAccount_ReturnsValidationError()
    {
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var unknownOwnAccountId = Guid.NewGuid();
        var harness = new HandlerHarness(Now);
        // No own account registered — GetAllByClientAsync returns empty

        var result = await harness.Handler.Handle(
            new AnalyzeProfileCommand(
                orgId,
                Guid.NewGuid(),
                ClientId: clientId,
                OwnAccountId: unknownOwnAccountId,
                ProfileUrl: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.own_account_not_found", result.Error.Code);
    }

    // ── IProfileScrapeResolver NOT called during command dispatch ─────────────

    [Fact]
    public async Task Handle_UrlPath_ScrapeResolverNotCalledDuringCommandDispatch()
    {
        var harness = new HandlerHarness(Now);

        await harness.Handler.Handle(
            new AnalyzeProfileCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                ClientId: null,
                OwnAccountId: null,
                ProfileUrl: "https://www.instagram.com/somebrand"),
            CancellationToken.None);

        // The scrape resolver must not be invoked at all during command-dispatch
        Assert.False(harness.ScrapeResolver.WasCalled,
            "IProfileScrapeResolver must not be called during command validation/dispatch.");
    }

    // ── Job is enqueued on success ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_ClientPath_EnqueuesAnalysisJob()
    {
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var ownAccountId = Guid.NewGuid();
        var harness = new HandlerHarness(Now);
        harness.OwnAccountRepo.AddAccount(ownAccountId, clientId, orgId, "https://instagram.com/testbrand");

        var result = await harness.Handler.Handle(
            new AnalyzeProfileCommand(
                orgId,
                Guid.NewGuid(),
                ClientId: clientId,
                OwnAccountId: ownAccountId,
                ProfileUrl: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(harness.JobQueue.Enqueued);
        Assert.Equal(result.Value.Id, harness.JobQueue.Enqueued[0].AnalysisId);
        Assert.Equal(orgId, harness.JobQueue.Enqueued[0].OrganizationId);
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private sealed class HandlerHarness
    {
        public HandlerHarness(DateTime nowUtc)
        {
            Clock = new TestClock(nowUtc);
            Repository = new FakeProfileAnalysisRepository();
            OwnAccountRepo = new FakeClientOwnAccountRepository();
            UnitOfWork = new TestUnitOfWork();
            ScrapeResolver = new TrackingScrapeResolver();
            JobQueue = new FakeProfileAnalysisJobQueue();

            Handler = new AnalyzeProfileHandler(
                Repository,
                OwnAccountRepo,
                UnitOfWork,
                Clock,
                JobQueue);
        }

        public TestClock Clock { get; }
        public FakeProfileAnalysisRepository Repository { get; }
        public FakeClientOwnAccountRepository OwnAccountRepo { get; }
        public TestUnitOfWork UnitOfWork { get; }
        public TrackingScrapeResolver ScrapeResolver { get; }
        public FakeProfileAnalysisJobQueue JobQueue { get; }
        public AnalyzeProfileHandler Handler { get; }
    }

    private sealed class FakeProfileAnalysisRepository : IProfileAnalysisRepository
    {
        public DomainProfileAnalysis? Stored { get; private set; }

        public Task AddAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default)
        {
            Stored = analysis;
            return Task.CompletedTask;
        }

        public Task<DomainProfileAnalysis?> GetByIdAsync(
            Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored?.Id == analysisId && Stored.OrganizationId == organizationId
                ? Stored : null);

        public Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(
            Guid organizationId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileAnalysisHistoryPage([], 0, page, pageSize));

        public Task UpdateAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default)
        {
            Stored = analysis;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(
            Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<DateTime?> GetLatestAnalyzedCompletedAtAsync(
            Guid orgId, Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DateTime?>(null);

        public Task<bool> HasNonTerminalAnalysisForClientAsync(
            Guid orgId, Guid clientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeClientOwnAccountRepository : IClientOwnAccountRepository
    {
        private readonly List<ClientOwnAccount> _accounts = [];

        public void AddAccount(Guid ownAccountId, Guid clientId, Guid orgId, string profileUrl)
        {
            var account = ClientOwnAccount.Restore(
                new ClientOwnAccountId(ownAccountId),
                new ClientId(clientId),
                orgId,
                SocialNetworkId.New(),
                profileUrl,
                handle: "testbrand",
                label: null,
                notes: null,
                isActive: true,
                createdAtUtc: DateTime.UtcNow,
                updatedAtUtc: DateTime.UtcNow);
            _accounts.Add(account);
        }

        public Task<ClientOwnAccount?> GetByIdAsync(ClientOwnAccountId id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult(_accounts.FirstOrDefault(a => a.Id == id && a.OrganizationId == organizationId));

        public Task<(IReadOnlyList<ClientOwnAccount> Items, int TotalCount)> ListByClientAsync(
            ClientId clientId, Guid organizationId, bool includeInactive, int page, int pageSize, CancellationToken ct)
        {
            var items = _accounts.Where(a => a.ClientId == clientId && a.OrganizationId == organizationId).ToList();
            return Task.FromResult<(IReadOnlyList<ClientOwnAccount>, int)>((items, items.Count));
        }

        public Task<IReadOnlyList<ClientOwnAccount>> GetAllByClientAsync(
            ClientId clientId, Guid organizationId, CancellationToken ct)
        {
            IReadOnlyList<ClientOwnAccount> items =
                _accounts.Where(a => a.ClientId == clientId && a.OrganizationId == organizationId).ToList();
            return Task.FromResult(items);
        }

        public Task<bool> ExistsByProfileUrlAsync(
            ClientId clientId, SocialNetworkId socialNetworkId, string profileUrl,
            ClientOwnAccountId? excludeId, CancellationToken ct) => Task.FromResult(false);

        public Task AddAsync(ClientOwnAccount ownAccount, CancellationToken ct) => Task.CompletedTask;

        public void Update(ClientOwnAccount account) { }

        public Task ReorderAsync(ClientId clientId, Guid organizationId,
            IReadOnlyList<Guid> orderedIds, CancellationToken ct) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TrackingScrapeResolver : IProfileScrapeResolver
    {
        public bool WasCalled { get; private set; }

        public Task<Result<ProfileScrapeResult>> ResolveAsync(ProfileScrapeRequest request, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(Result.Failure<ProfileScrapeResult>(
                new Error("test.not_expected", "Scrape resolver should not be called", ErrorType.Failure)));
        }
    }

    private sealed class FakeProfileAnalysisJobQueue : IProfileAnalysisJobQueue
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

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
