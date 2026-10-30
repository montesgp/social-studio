using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.ContentPlans.GetByPeriod;
using SocialStudio.Application.Features.Social.ContentPlans.Upsert;
using SocialStudio.Application.Features.Social.GeneralStrategies.GetByClientId;
using SocialStudio.Application.Features.Social.GeneralStrategies.Upsert;
using SocialStudio.Domain.Social.GeneralStrategies;
using SocialStudio.Tests.Common;
using ContentPlanHarness = SocialStudio.Tests.Application.Social.ContentPlans.ContentPlanLifecycleTests;
using DomainGeneralStrategy = SocialStudio.Domain.Social.GeneralStrategies.GeneralStrategy;

namespace SocialStudio.Tests.Application.Social.GeneralStrategy;

/// <summary>
/// Application-layer lifecycle tests for GeneralStrategy.
/// Covers GSTRAT-01 scenarios: upsert-create, upsert-overwrite, empty-signal rejection,
/// field-length violations, get-by-client, and tenant/ownership boundary.
/// Uses an in-memory repository — no EF dependency.
/// </summary>
public sealed class GeneralStrategyLifecycleTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc);

    // ─── Upsert: create path ─────────────────────────────────────────────────

    [Fact]
    public async Task UpsertHandler_NoExisting_CreatesNewStrategy()
    {
        var harness = new Harness(Now);
        var clientId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var result = await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(
                clientId, orgId,
                ContentPillars: ["Education", "Inspiration"],
                TargetAudience: "Developers",
                PostingFrequency: "3x per week",
                Notes: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(clientId, result.Value.ClientId);
        Assert.Equal(orgId, result.Value.OrganizationId);
        Assert.Equal(2, result.Value.ContentPillars.Count);
        Assert.Contains("Education", result.Value.ContentPillars);
        Assert.Equal("Developers", result.Value.TargetAudience);
        Assert.Equal(Now, result.Value.CreatedAtUtc);
        Assert.Equal(Now, result.Value.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpsertHandler_NoExisting_StrategyIsPersisted()
    {
        var harness = new Harness(Now);
        var clientId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(
                clientId, orgId,
                ContentPillars: ["Education"],
                TargetAudience: null,
                PostingFrequency: null,
                Notes: null),
            CancellationToken.None);

        var fetched = await harness.Repo.GetByClientIdAsync(clientId, orgId, CancellationToken.None);
        Assert.NotNull(fetched);
        Assert.Contains("Education", fetched.ContentPillars);
    }

    // ─── Upsert: update path ─────────────────────────────────────────────────

    [Fact]
    public async Task UpsertHandler_ExistingStrategy_OverwritesAndAdvancesUpdatedAt()
    {
        var harness = new Harness(Now);
        var clientId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        // First upsert — creates
        await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(
                clientId, orgId,
                ContentPillars: ["Old Pillar"],
                TargetAudience: null,
                PostingFrequency: null,
                Notes: null),
            CancellationToken.None);

        // Advance clock
        harness.Clock.UtcNow = Later;

        // Second upsert — updates
        var result = await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(
                clientId, orgId,
                ContentPillars: ["New Pillar A", "New Pillar B"],
                TargetAudience: "Marketers",
                PostingFrequency: null,
                Notes: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.ContentPillars.Count);
        Assert.Contains("New Pillar A", result.Value.ContentPillars);
        Assert.DoesNotContain("Old Pillar", result.Value.ContentPillars);
        Assert.Equal(Later, result.Value.UpdatedAtUtc);
        Assert.Equal(Now, result.Value.CreatedAtUtc); // CreatedAt must NOT change
    }

    [Fact]
    public async Task UpsertHandler_ExistingStrategy_OnlyOneRecordExists()
    {
        var harness = new Harness(Now);
        var clientId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(clientId, orgId, ["Pillar1"], null, null, null),
            CancellationToken.None);

        await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(clientId, orgId, ["Pillar2"], null, null, null),
            CancellationToken.None);

        // Repository should still have exactly one entry for this client
        var count = harness.Repo.CountByClient(clientId, orgId);
        Assert.Equal(1, count);
    }

    // ─── Empty-signal rejection ──────────────────────────────────────────────

    [Fact]
    public async Task UpsertHandler_AllFieldsNullOrEmpty_ReturnsEmptyStrategyError()
    {
        var harness = new Harness(Now);

        var result = await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(
                Guid.NewGuid(), Guid.NewGuid(),
                ContentPillars: null,
                TargetAudience: null,
                PostingFrequency: null,
                Notes: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.general_strategies.empty_strategy", result.Error.Code);
    }

    [Fact]
    public async Task UpsertHandler_AllFieldsWhitespace_ReturnsEmptyStrategyError()
    {
        var harness = new Harness(Now);

        var result = await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(
                Guid.NewGuid(), Guid.NewGuid(),
                ContentPillars: [],
                TargetAudience: "   ",
                PostingFrequency: "   ",
                Notes: "   "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.general_strategies.empty_strategy", result.Error.Code);
    }

    // ─── Field length violations ─────────────────────────────────────────────

    [Fact]
    public async Task UpsertHandler_ContentPillarsExceedMax_ReturnsValidationError()
    {
        var harness = new Harness(Now);
        var pillars = Enumerable.Range(1, DomainGeneralStrategy.ContentPillarsMaxCount + 1)
            .Select(i => $"Pillar {i}")
            .ToList();

        var result = await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(
                Guid.NewGuid(), Guid.NewGuid(),
                ContentPillars: pillars,
                TargetAudience: null,
                PostingFrequency: null,
                Notes: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.general_strategies.validation", result.Error.Code);
    }

    // ─── Get by client ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHandler_ExistingStrategy_ReturnsFullResponse()
    {
        var harness = new Harness(Now);
        var clientId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(
                clientId, orgId,
                ContentPillars: ["Growth"],
                TargetAudience: "Startups",
                PostingFrequency: "Daily",
                Notes: "Internal note"),
            CancellationToken.None);

        var result = await harness.Get.Handle(
            new GetGeneralStrategyQuery(clientId, orgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(clientId, result.Value.ClientId);
        Assert.Equal(orgId, result.Value.OrganizationId);
        Assert.Contains("Growth", result.Value.ContentPillars);
        Assert.Equal("Startups", result.Value.TargetAudience);
        Assert.Equal("Daily", result.Value.PostingFrequency);
        Assert.Equal("Internal note", result.Value.Notes);
    }

    [Fact]
    public async Task GetHandler_NoStrategy_ReturnsNotFound()
    {
        var harness = new Harness(Now);

        var result = await harness.Get.Handle(
            new GetGeneralStrategyQuery(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.general_strategies.not_found", result.Error.Code);
    }

    // ─── Tenant / ownership boundary ─────────────────────────────────────────

    [Fact]
    public async Task GetHandler_WrongOrganization_ReturnsNotFound()
    {
        var harness = new Harness(Now);
        var clientId = Guid.NewGuid();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        // Seed under orgA
        await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(
                clientId, orgA,
                ContentPillars: ["Pillar"],
                TargetAudience: null,
                PostingFrequency: null,
                Notes: null),
            CancellationToken.None);

        // Query with orgB — must not leak across tenant boundary
        var result = await harness.Get.Handle(
            new GetGeneralStrategyQuery(clientId, orgB),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.general_strategies.not_found", result.Error.Code);
    }

    [Fact]
    public async Task UpsertHandler_TwoClientsInSameOrg_StoresSeparateRecords()
    {
        var harness = new Harness(Now);
        var orgId = Guid.NewGuid();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();

        await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(clientA, orgId, ["Pillar A"], null, null, null),
            CancellationToken.None);

        await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(clientB, orgId, ["Pillar B"], null, null, null),
            CancellationToken.None);

        var resultA = await harness.Get.Handle(new GetGeneralStrategyQuery(clientA, orgId), CancellationToken.None);
        var resultB = await harness.Get.Handle(new GetGeneralStrategyQuery(clientB, orgId), CancellationToken.None);

        Assert.True(resultA.IsSuccess);
        Assert.True(resultB.IsSuccess);
        Assert.Contains("Pillar A", resultA.Value.ContentPillars);
        Assert.Contains("Pillar B", resultB.Value.ContentPillars);
        Assert.NotEqual(resultA.Value.Id, resultB.Value.Id);
    }

    // ─── GSTRAT-02: updating the strategy does NOT cascade to Content Plans ──────

    [Fact]
    public async Task UpsertHandler_UpdatingStrategy_DoesNotCascadeToContentPlans()
    {
        var clientId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        // Seed a Content Plan for the same client (a separate aggregate, separate repo).
        var planRepo = new ContentPlanHarness.InMemoryContentPlanRepository();
        var planUpsert = new UpsertContentPlanCommandHandler(planRepo, new TestClock(Now));
        var planGet = new GetContentPlanHandler(planRepo);

        await planUpsert.Handle(
            new UpsertContentPlanCommand(
                clientId, orgId, 2026, 6,
                Objectives: "Original objective",
                Kpis: null,
                ContentStrategyText: "Original content strategy",
                Slots: null),
            CancellationToken.None);

        var before = await planGet.Handle(new GetContentPlanQuery(clientId, orgId, 2026, 6), CancellationToken.None);
        Assert.True(before.IsSuccess);

        // Create then update the General Strategy on a later clock.
        var harness = new Harness(Now);
        await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(clientId, orgId, ["Old direction"], null, null, null),
            CancellationToken.None);

        harness.Clock.UtcNow = Later;
        await harness.Upsert.Handle(
            new UpsertGeneralStrategyCommand(clientId, orgId, ["New direction"], "New audience", null, null),
            CancellationToken.None);

        // The Content Plan must be byte-for-byte untouched — no cascade from the strategy change.
        var after = await planGet.Handle(new GetContentPlanQuery(clientId, orgId, 2026, 6), CancellationToken.None);
        Assert.True(after.IsSuccess);
        Assert.Equal("Original objective", after.Value.ObjectiveText);
        Assert.Equal("Original content strategy", after.Value.ContentStrategyText);
        Assert.Equal(before.Value.UpdatedAtUtc, after.Value.UpdatedAtUtc);
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public Harness(DateTime nowUtc)
        {
            Clock = new TestClock(nowUtc);
            Repo = new InMemoryGeneralStrategyRepository();

            Upsert = new UpsertGeneralStrategyCommandHandler(Repo, Clock);
            Get = new GetGeneralStrategyHandler(Repo);
        }

        public TestClock Clock { get; }
        public InMemoryGeneralStrategyRepository Repo { get; }
        public UpsertGeneralStrategyCommandHandler Upsert { get; }
        public GetGeneralStrategyHandler Get { get; }
    }

    // ─── In-memory repository ─────────────────────────────────────────────────

    internal sealed class InMemoryGeneralStrategyRepository : IGeneralStrategyRepository
    {
        private readonly Dictionary<(Guid ClientId, Guid OrgId), DomainGeneralStrategy> _store = new();

        public Task<DomainGeneralStrategy?> GetByClientIdAsync(
            Guid clientId,
            Guid organizationId,
            CancellationToken ct)
        {
            _store.TryGetValue((clientId, organizationId), out var strategy);
            return Task.FromResult(strategy);
        }

        public void Add(DomainGeneralStrategy strategy)
        {
            _store[(strategy.ClientId, strategy.OrganizationId)] = strategy;
        }

        public void Update(DomainGeneralStrategy strategy)
        {
            _store[(strategy.ClientId, strategy.OrganizationId)] = strategy;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

        /// <summary>Helper for asserting uniqueness — counts entries for a specific client+org pair.</summary>
        public int CountByClient(Guid clientId, Guid orgId)
            => _store.Keys.Count(k => k.ClientId == clientId && k.OrgId == orgId);
    }
}
