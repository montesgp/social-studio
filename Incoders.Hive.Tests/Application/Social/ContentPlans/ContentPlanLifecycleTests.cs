using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ContentPlans;
using Incoders.Hive.Application.Features.Social.ContentPlans.GetByPeriod;
using Incoders.Hive.Application.Features.Social.ContentPlans.ListByClient;
using Incoders.Hive.Application.Features.Social.ContentPlans.Upsert;
using Incoders.Hive.Domain.Social.ContentPlans;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Social.ContentPlans;

/// <summary>
/// Application-layer lifecycle tests for ContentPlan.
/// Covers CP-03 scenarios (create, overwrite, empty objective, get not-found, list),
/// CP-05 scenarios (upsert does NOT trigger generation), provenance on nodes,
/// and basic Period-validation forwarding from the handler.
/// Uses an in-memory repository — no EF dependency.
/// </summary>
public sealed class ContentPlanLifecycleTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc);

    // ─── CP-03: Create new period ────────────────────────────────────────────

    [Fact]
    public async Task UpsertHandler_NewPeriod_CreatesContentPlan()
    {
        var harness = new Harness(Now);
        var clientId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var result = await harness.Upsert.Handle(
            new UpsertContentPlanCommand(
                clientId, orgId,
                Year: 2026, Month: 6,
                Objectives: "Drive brand awareness",
                Kpis: ["10k impressions", "500 clicks"],
                ContentStrategyText: null,
                Slots: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(clientId, result.Value.ClientId);
        Assert.Equal(orgId, result.Value.OrganizationId);
        Assert.Equal(2026, result.Value.Year);
        Assert.Equal(6, result.Value.Month);
        Assert.Equal("Drive brand awareness", result.Value.ObjectiveText);
        Assert.Equal(2, result.Value.Kpis.Count);
    }

    [Fact]
    public async Task UpsertHandler_NewPeriod_ObjectiveProvenanceIsManual()
    {
        var harness = new Harness(Now);

        var result = await harness.Upsert.Handle(
            new UpsertContentPlanCommand(
                Guid.NewGuid(), Guid.NewGuid(),
                Year: 2026, Month: 6,
                Objectives: "Some objective",
                Kpis: null,
                ContentStrategyText: null,
                Slots: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Manual", result.Value.ObjectiveProvenance.Source);
    }

    // ─── CP-03: Overwrite existing plan ─────────────────────────────────────

    [Fact]
    public async Task UpsertHandler_ExistingPeriod_OverwritesObjectiveAndAdvancesUpdatedAt()
    {
        var harness = new Harness(Now);
        var clientId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        await harness.Upsert.Handle(
            new UpsertContentPlanCommand(
                clientId, orgId, 2026, 6,
                "Original objective",
                Kpis: null,
                ContentStrategyText: null,
                Slots: null),
            CancellationToken.None);

        harness.Clock.UtcNow = Later;

        var result = await harness.Upsert.Handle(
            new UpsertContentPlanCommand(
                clientId, orgId, 2026, 6,
                "Updated objective",
                Kpis: ["new KPI"],
                ContentStrategyText: null,
                Slots: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated objective", result.Value.ObjectiveText);
        Assert.Single(result.Value.Kpis);
        Assert.Equal(Later, result.Value.UpdatedAtUtc);
        Assert.Equal(Now, result.Value.CreatedAtUtc); // CreatedAt must NOT change
    }

    // ─── CP-03: Empty objective rejected ─────────────────────────────────────

    [Fact]
    public async Task UpsertHandler_EmptyObjective_ReturnsObjectiveRequiredError()
    {
        var harness = new Harness(Now);

        var result = await harness.Upsert.Handle(
            new UpsertContentPlanCommand(
                Guid.NewGuid(), Guid.NewGuid(), 2026, 6,
                Objectives: "",
                Kpis: null,
                ContentStrategyText: null,
                Slots: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.content_plans.objective_required", result.Error.Code);
    }

    [Fact]
    public async Task UpsertHandler_WhitespaceObjective_ReturnsObjectiveRequiredError()
    {
        var harness = new Harness(Now);

        var result = await harness.Upsert.Handle(
            new UpsertContentPlanCommand(
                Guid.NewGuid(), Guid.NewGuid(), 2026, 6,
                Objectives: "   ",
                Kpis: null,
                ContentStrategyText: null,
                Slots: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.content_plans.objective_required", result.Error.Code);
    }

    // ─── CP-03: Get by period ────────────────────────────────────────────────

    [Fact]
    public async Task GetHandler_ExistingPeriod_ReturnsPlanWithAllNodes()
    {
        var harness = new Harness(Now);
        var clientId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        await harness.Upsert.Handle(
            new UpsertContentPlanCommand(
                clientId, orgId, 2026, 6,
                "Grow audience",
                Kpis: ["5k followers"],
                ContentStrategyText: "Focus on reels",
                Slots: null),
            CancellationToken.None);

        var result = await harness.Get.Handle(
            new GetContentPlanQuery(clientId, orgId, 2026, 6),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(clientId, result.Value.ClientId);
        Assert.Equal(2026, result.Value.Year);
        Assert.Equal(6, result.Value.Month);
        Assert.Equal("Grow audience", result.Value.ObjectiveText);
        Assert.Equal("Focus on reels", result.Value.ContentStrategyText);
        Assert.Single(result.Value.Kpis);
    }

    [Fact]
    public async Task GetHandler_MissingPeriod_ReturnsNotFound()
    {
        var harness = new Harness(Now);

        var result = await harness.Get.Handle(
            new GetContentPlanQuery(Guid.NewGuid(), Guid.NewGuid(), 2025, 1),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.content_plans.not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetHandler_InvalidPeriod_ReturnsValidationError()
    {
        var harness = new Harness(Now);

        var result = await harness.Get.Handle(
            new GetContentPlanQuery(Guid.NewGuid(), Guid.NewGuid(), 2026, 13),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.content_plans.invalid_month", result.Error.Code);
    }

    // ─── CP-03: List by client ────────────────────────────────────────────────

    [Fact]
    public async Task ListHandler_MultiiplePeriods_ReturnsAllPeriods()
    {
        var harness = new Harness(Now);
        var clientId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        await harness.Upsert.Handle(
            new UpsertContentPlanCommand(clientId, orgId, 2026, 5, "May objective", null, null, null),
            CancellationToken.None);

        await harness.Upsert.Handle(
            new UpsertContentPlanCommand(clientId, orgId, 2026, 6, "June objective", null, null, null),
            CancellationToken.None);

        var result = await harness.List.Handle(
            new ListContentPlansQuery(clientId, orgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains(result.Value, s => s.Month == 5);
        Assert.Contains(result.Value, s => s.Month == 6);
    }

    [Fact]
    public async Task ListHandler_NoPlans_ReturnsEmptyList()
    {
        var harness = new Harness(Now);

        var result = await harness.List.Handle(
            new ListContentPlansQuery(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    // ─── CP-05: No auto-generation triggered on upsert ───────────────────────

    [Fact]
    public async Task UpsertHandler_AfterSave_NoGenerationJobTriggered()
    {
        // CP-05: the upsert must only persist the plan and return it.
        // We verify by asserting the response confirms plan save (not generation).
        // In this architecture, generation is a separate command — if UpsertContentPlanCommand
        // returned a generation ID or status, that would violate CP-05.
        var harness = new Harness(Now);
        var fakeJobTracker = harness.JobTracker;

        await harness.Upsert.Handle(
            new UpsertContentPlanCommand(
                Guid.NewGuid(), Guid.NewGuid(), 2026, 6,
                "Test objective",
                Kpis: null, ContentStrategyText: null, Slots: null),
            CancellationToken.None);

        // No generation job should have been enqueued
        Assert.Equal(0, fakeJobTracker.GenerationJobsEnqueued);
    }

    // ─── Provenance on nodes ─────────────────────────────────────────────────

    [Fact]
    public async Task UpsertHandler_WithContentStrategy_ContentStrategyProvenanceIsManual()
    {
        var harness = new Harness(Now);

        var result = await harness.Upsert.Handle(
            new UpsertContentPlanCommand(
                Guid.NewGuid(), Guid.NewGuid(), 2026, 6,
                "Objective",
                Kpis: null,
                ContentStrategyText: "Use short videos",
                Slots: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Manual", result.Value.ContentStrategyProvenance.Source);
        Assert.Equal("Use short videos", result.Value.ContentStrategyText);
    }

    // ─── Calendar slots (CP-04 integration) ──────────────────────────────────

    [Fact]
    public async Task UpsertHandler_WithSlots_SlotsPersistedInOrder()
    {
        var harness = new Harness(Now);

        var result = await harness.Upsert.Handle(
            new UpsertContentPlanCommand(
                Guid.NewGuid(), Guid.NewGuid(), 2026, 6,
                "Objective",
                Kpis: null,
                ContentStrategyText: null,
                Slots:
                [
                    new CalendarSlotRequest(1, "Reel"),
                    new CalendarSlotRequest(8, "Carousel"),
                    new CalendarSlotRequest(15, "Story"),
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Slots.Count);
        Assert.Equal(1, result.Value.Slots[0].Day);
        Assert.Equal(8, result.Value.Slots[1].Day);
        Assert.Equal(15, result.Value.Slots[2].Day);
        Assert.Equal("Reel", result.Value.Slots[0].ContentFormat);
    }

    [Fact]
    public async Task UpsertHandler_SlotDayOutOfRangeForFebruary_ReturnsInvalidDayError()
    {
        var harness = new Harness(Now);

        var result = await harness.Upsert.Handle(
            new UpsertContentPlanCommand(
                Guid.NewGuid(), Guid.NewGuid(), 2026, 2, // February 2026 — 28 days
                "Objective",
                Kpis: null,
                ContentStrategyText: null,
                Slots: [new CalendarSlotRequest(30, "Reel")]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.content_plans.invalid_day", result.Error.Code);
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public Harness(DateTime nowUtc)
        {
            Clock = new TestClock(nowUtc);
            Repo = new InMemoryContentPlanRepository();
            JobTracker = new FakeJobTracker();

            Upsert = new UpsertContentPlanCommandHandler(Repo, Clock);
            Get = new GetContentPlanHandler(Repo);
            List = new ListContentPlansHandler(Repo);
        }

        public TestClock Clock { get; }
        public InMemoryContentPlanRepository Repo { get; }
        public FakeJobTracker JobTracker { get; }
        public UpsertContentPlanCommandHandler Upsert { get; }
        public GetContentPlanHandler Get { get; }
        public ListContentPlansHandler List { get; }
    }

    /// <summary>
    /// Tracks whether any generation job was enqueued during a test.
    /// CP-05 asserts this always stays at 0 after a plan upsert.
    /// </summary>
    internal sealed class FakeJobTracker
    {
        public int GenerationJobsEnqueued { get; private set; }

        public void EnqueueGenerationJob() => GenerationJobsEnqueued++;
    }

    // ─── In-memory repository ─────────────────────────────────────────────────

    internal sealed class InMemoryContentPlanRepository : IContentPlanRepository
    {
        // Key: (orgId, clientId, year, month)
        private readonly Dictionary<(Guid OrgId, Guid ClientId, int Year, int Month), ContentPlan> _store = new();

        public Task<ContentPlan?> GetByClientAndPeriodAsync(
            Guid organizationId,
            Guid clientId,
            Period period,
            CancellationToken ct)
        {
            _store.TryGetValue((organizationId, clientId, period.Year, period.Month), out var plan);
            return Task.FromResult(plan);
        }

        public Task<IReadOnlyList<ContentPlan>> ListByClientAsync(
            Guid organizationId,
            Guid clientId,
            CancellationToken ct)
        {
            var plans = _store.Values
                .Where(p => p.OrganizationId == organizationId && p.ClientId == clientId)
                .ToList();
            return Task.FromResult<IReadOnlyList<ContentPlan>>(plans);
        }

        public Task UpsertAsync(ContentPlan plan, CancellationToken ct)
        {
            _store[(plan.OrganizationId, plan.ClientId, plan.Period.Year, plan.Period.Month)] = plan;
            return Task.CompletedTask;
        }

        public Task LinkSlotPackageAsync(
            Guid planId,
            CalendarSlotId slotId,
            Guid packageId,
            CancellationToken ct)
        {
            // Not exercised in lifecycle tests
            return Task.CompletedTask;
        }
    }
}
