using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;
using Incoders.Hive.Domain.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>WU-11b (design §10): ranked opportunity list for a run.</summary>
public sealed class ListRunOpportunitiesQueryTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_CompletedRun_ReturnsTheRankedOpportunities()
    {
        var run = CreateRun(DiscoveryRunStatus.Completed);
        var record = OpportunityRecord(run.Id.Value);
        var handler = new ListRunOpportunitiesHandler(
            new FakeRunRepository(run), new FakeOpportunityRepository([record]));

        var result = await handler.Handle(new ListRunOpportunitiesQuery(Org, run.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Opportunities);
        // WU-12 gap fix: the dashboard sorts/filters by target-market competition, automation
        // suitability, and trend (ND-12) directly from the run's opportunity list — the summary
        // projection did not carry these before WU-12 needed them.
        var opportunity = result.Value.Opportunities[0];
        Assert.Equal(record.TargetMarketCompetition, opportunity.TargetMarketCompetition);
        Assert.Equal(record.AutomationSuitability, opportunity.AutomationSuitability);
        Assert.Equal(record.GrowthProxy, opportunity.GrowthProxy);
    }

    [Fact]
    public async Task Handle_RunStillRunning_ReturnsRunAlreadyRunning()
    {
        var run = CreateRun(DiscoveryRunStatus.Running);
        var handler = new ListRunOpportunitiesHandler(
            new FakeRunRepository(run), new FakeOpportunityRepository([]));

        var result = await handler.Handle(new ListRunOpportunitiesQuery(Org, run.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.RunAlreadyRunning.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_UnknownRun_ReturnsRunNotFound()
    {
        var handler = new ListRunOpportunitiesHandler(
            new FakeRunRepository(CreateRun(DiscoveryRunStatus.Completed)), new FakeOpportunityRepository([]));

        var result = await handler.Handle(new ListRunOpportunitiesQuery(Org, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.RunNotFound.Code, result.Error.Code);
    }

    private static DiscoveryRun CreateRun(DiscoveryRunStatus status)
    {
        var result = DiscoveryRun.Create(
            Org, User, "es-AR", "pt-BR", "youtube", "video",
            DiscoveryWindow.Default(), DiscoveryDepth.Standard, "{}", nicheId: null, Now);
        Assert.True(result.IsSuccess);
        var run = result.Value;

        if (status is DiscoveryRunStatus.Running or DiscoveryRunStatus.Completed or DiscoveryRunStatus.PartiallyCompleted)
        {
            run.Start(Now);
        }

        if (status == DiscoveryRunStatus.Completed)
        {
            run.Complete(Now);
        }

        return run;
    }

    private static NicheOpportunityRecord OpportunityRecord(Guid runId) => new(
        Guid.NewGuid(), runId, Org, null, "Budget travel", "Listicle", ["budget-travel"],
        0.7, 0.6, 0.9, "[]", 10000, 1.5, 0.2, 1.1, 3, 0.4, 0.3, 0.28, 1250.0, 0.5, 3, 3, 1, Now);

    private sealed class FakeRunRepository : IDiscoveryRunRepository
    {
        private readonly DiscoveryRun _run;

        public FakeRunRepository(DiscoveryRun run) => _run = run;

        public Task AddAsync(DiscoveryRun run, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(DiscoveryRun run, CancellationToken ct = default) => Task.CompletedTask;

        public Task<DiscoveryRun?> GetByIdAsync(Guid organizationId, DiscoveryRunId id, CancellationToken ct = default) =>
            Task.FromResult(id == _run.Id && organizationId == _run.OrganizationId ? _run : null);

        public Task<IReadOnlyList<DiscoveryRun>> ListByOrganizationAsync(Guid organizationId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DiscoveryRun>>(
                _run.OrganizationId == organizationId ? [_run] : []);
    }

    private sealed class FakeOpportunityRepository : INicheOpportunityRepository
    {
        private readonly List<NicheOpportunityRecord> _records;

        public FakeOpportunityRepository(List<NicheOpportunityRecord> records) => _records = records;

        public Task AppendAsync(IReadOnlyList<NicheOpportunityRecord> opportunities, CancellationToken ct = default)
        {
            _records.AddRange(opportunities);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NicheOpportunityRecord>> ListByRunAsync(Guid organizationId, Guid runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NicheOpportunityRecord>>(_records.Where(r => r.RunId == runId).ToList());

        public Task<NicheOpportunityRecord?> GetByIdAsync(Guid organizationId, Guid opportunityId, CancellationToken ct = default) =>
            Task.FromResult(_records.FirstOrDefault(r => r.Id == opportunityId));
    }
}
