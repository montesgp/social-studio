using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;
using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// The organization's run history. A run costs provider quota, AI completions and review time, and it
/// was already persisted per organization — this is what makes it reachable again after the operator
/// closes the tool.
/// </summary>
public sealed class ListDiscoveryRunsQueryTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid OtherOrg = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_ReturnsThisOrganizationsRuns()
    {
        var handler = new ListDiscoveryRunsHandler(new FakeRunRepository([Run(Org), Run(Org)]));

        var result = await handler.Handle(new ListDiscoveryRunsQuery(Org, 25), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Runs.Count);
    }

    /// <summary>Tenant isolation: history is one of the easiest places to leak another org's work.</summary>
    [Fact]
    public async Task Handle_NeverReturnsAnotherOrganizationsRuns()
    {
        var repository = new FakeRunRepository([Run(Org), Run(OtherOrg), Run(OtherOrg)]);
        var handler = new ListDiscoveryRunsHandler(repository);

        var result = await handler.Handle(new ListDiscoveryRunsQuery(Org, 25), CancellationToken.None);

        Assert.Single(result.Value.Runs);
        Assert.Equal(Org, repository.LastRequestedOrganizationId);
    }

    /// <summary>A long-lived organization must not turn the history into an unbounded query.</summary>
    [Fact]
    public async Task Handle_ClampsAnOversizedLimit()
    {
        var repository = new FakeRunRepository([]);
        var handler = new ListDiscoveryRunsHandler(repository);

        await handler.Handle(new ListDiscoveryRunsQuery(Org, 10_000), CancellationToken.None);

        Assert.Equal(ListDiscoveryRunsHandler.MaxLimit, repository.LastRequestedLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Handle_FallsBackToTheDefaultLimit_WhenNoneIsUsable(int limit)
    {
        var repository = new FakeRunRepository([]);
        var handler = new ListDiscoveryRunsHandler(repository);

        await handler.Handle(new ListDiscoveryRunsQuery(Org, limit), CancellationToken.None);

        Assert.Equal(ListDiscoveryRunsHandler.DefaultLimit, repository.LastRequestedLimit);
    }

    /// <summary>
    /// Seed terms are what separates two otherwise identical rows: an open discovery run and a targeted
    /// one look the same without them.
    /// </summary>
    [Fact]
    public async Task Handle_CarriesTheSeedTermsThatDistinguishOneRunFromAnother()
    {
        var handler = new ListDiscoveryRunsHandler(
            new FakeRunRepository([Run(Org, seedTerms: ["budget travel"]), Run(Org, seedTerms: [])]));

        var result = await handler.Handle(new ListDiscoveryRunsQuery(Org, 25), CancellationToken.None);

        Assert.Contains(result.Value.Runs, r => r.SeedTerms.Contains("budget travel"));
        Assert.Contains(result.Value.Runs, r => r.SeedTerms.Count == 0);
    }

    /// <summary>Failures belong in the history — "the run I started yesterday broke" is a real question.</summary>
    [Fact]
    public async Task Handle_IncludesRunsThatFailed()
    {
        var failed = Run(Org);
        failed.Start(Now);
        failed.Fail(NicheDiscoveryErrors.OriginUnconfigured.Code, Now);

        var handler = new ListDiscoveryRunsHandler(new FakeRunRepository([failed]));

        var result = await handler.Handle(new ListDiscoveryRunsQuery(Org, 25), CancellationToken.None);

        var entry = Assert.Single(result.Value.Runs);
        Assert.Equal(DiscoveryRunStatus.Failed.ToString(), entry.Status);
        Assert.Equal(NicheDiscoveryErrors.OriginUnconfigured.Code, entry.FailureReasonKey);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static DiscoveryRun Run(Guid organizationId, IReadOnlyList<string>? seedTerms = null)
    {
        var snapshot = new DiscoveryRunConfigurationSnapshot(seedTerms ?? ["budget travel"]).ToJson();

        var result = DiscoveryRun.Create(
            organizationId, User, "es-ZZ", "pt-ZZ", "youtube", "video",
            DiscoveryWindow.Default(), DiscoveryDepth.Standard, snapshot, nicheId: null, Now);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class FakeRunRepository : IDiscoveryRunRepository
    {
        private readonly IReadOnlyList<DiscoveryRun> _runs;

        public FakeRunRepository(IReadOnlyList<DiscoveryRun> runs) => _runs = runs;

        public Guid? LastRequestedOrganizationId { get; private set; }

        public int? LastRequestedLimit { get; private set; }

        public Task AddAsync(DiscoveryRun run, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(DiscoveryRun run, CancellationToken ct = default) => Task.CompletedTask;

        public Task<DiscoveryRun?> GetByIdAsync(Guid organizationId, DiscoveryRunId id, CancellationToken ct = default) =>
            Task.FromResult(_runs.FirstOrDefault(r => r.Id == id && r.OrganizationId == organizationId));

        public Task<IReadOnlyList<DiscoveryRun>> ListByOrganizationAsync(
            Guid organizationId, int limit, CancellationToken ct = default)
        {
            LastRequestedOrganizationId = organizationId;
            LastRequestedLimit = limit;

            return Task.FromResult<IReadOnlyList<DiscoveryRun>>(
                _runs.Where(r => r.OrganizationId == organizationId).Take(limit).ToList());
        }
    }
}
