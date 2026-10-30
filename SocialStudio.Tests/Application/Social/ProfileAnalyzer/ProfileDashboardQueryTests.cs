using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.ContentPlans;
using SocialStudio.Application.Features.Social.ProfileAnalyzer.Analyze;
using SocialStudio.Application.Features.Social.ProfileAnalyzer.Dashboard;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentPlans;
using SocialStudio.Domain.Social.ProfileAnalysis;
using DomainProfileAnalysis = SocialStudio.Domain.Social.ProfileAnalysis.ProfileAnalysis;
using DomainGeneralStrategy = SocialStudio.Domain.Social.GeneralStrategies.GeneralStrategy;
using DomainGeneralStrategyId = SocialStudio.Domain.Social.GeneralStrategies.GeneralStrategyId;

namespace SocialStudio.Tests.Application.Social.ProfileAnalyzer;

/// <summary>
/// Unit tests for GetProfileDashboardQuery (PA-16, PA-17, PA-18) and
/// GetClientPlanComparisonQuery (PA-17).
/// </summary>
public sealed class ProfileDashboardQueryTests
{
    private static readonly DateTime Now = new(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    // ── PA-16: Analyzed record projects all sections ──────────────────────────

    [Fact]
    public async Task GetDashboard_AnalyzedRecord_ProjectsAllSections()
    {
        var analysis = BuildAnalyzedAnalysis(OrgId, ClientId, hasStats: true, hasAi: true);
        var harness = new DashboardHarness(analysis);

        var result = await harness.Handler.Handle(
            new GetProfileDashboardQuery(analysis.Id, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value;
        Assert.Equal(analysis.Id, dto.Id);
        Assert.Equal(OrgId, dto.OrganizationId);
        Assert.Equal(ClientId, dto.ClientId);
        Assert.Equal("Analyzed", dto.Status);
        Assert.NotNull(dto.Stats);
        Assert.NotEmpty(dto.OwnerPosts);
    }

    // ── PA-18: Null MedianViews does not throw; returned as null ─────────────

    [Fact]
    public async Task GetDashboard_NullMedianViews_ReturnsNullNotError()
    {
        var analysis = BuildAnalyzedAnalysis(OrgId, ClientId, hasStats: true, hasAi: true, medianViewsNull: true);
        var harness = new DashboardHarness(analysis);

        var result = await harness.Handler.Handle(
            new GetProfileDashboardQuery(analysis.Id, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Stats);
        Assert.Null(result.Value.Stats!.MedianViews);
    }

    // ── PA-17: URL-path (clientId == null) → Comparison is null ─────────────

    [Fact]
    public async Task GetDashboard_UrlPathAnalysis_ComparisonIsNull()
    {
        // URL-path analysis has no ClientId
        var analysis = BuildAnalyzedAnalysis(OrgId, clientId: null, hasStats: true, hasAi: true);
        var harness = new DashboardHarness(analysis);

        var result = await harness.Handler.Handle(
            new GetProfileDashboardQuery(analysis.Id, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Comparison);
        // Comparison repos must not have been queried
        Assert.False(harness.ContentPlanRepo.WasQueried,
            "ContentPlan repo must NOT be queried when clientId is null.");
        Assert.False(harness.GeneralStrategyRepo.WasQueried,
            "GeneralStrategy repo must NOT be queried when clientId is null.");
    }

    // ── PA-17: Client with ContentPlan + GeneralStrategy → Comparison present ─

    [Fact]
    public async Task GetDashboard_ClientWithPlanAndStrategy_ComparisonPresent()
    {
        var analysis = BuildAnalyzedAnalysis(OrgId, ClientId, hasStats: true, hasAi: true);
        var harness = new DashboardHarness(analysis);
        harness.ContentPlanRepo.AddPlan(ClientId, OrgId, "Monthly goal", ["KPI-1"], "Strategy text");
        harness.GeneralStrategyRepo.AddStrategy(ClientId, OrgId,
            ["Pillar A", "Pillar B"], "Marketers 25-35", "Tues/Thu");

        var result = await harness.Handler.Handle(
            new GetProfileDashboardQuery(analysis.Id, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var cmp = result.Value.Comparison;
        Assert.NotNull(cmp);
        Assert.Equal("Monthly goal", cmp!.ContentPlanObjective);
        Assert.Contains("KPI-1", cmp.ContentPlanKpis);
        // ContentPlanStrategy may be null for a freshly-created plan without a strategy text
        Assert.Contains("Pillar A", cmp.GeneralStrategyPillars);
        Assert.Equal("Marketers 25-35", cmp.GeneralStrategyAudience);
        Assert.Equal("Tues/Thu", cmp.GeneralStrategyPostingFrequency);
    }

    // ── PA-17: Client with NO ContentPlan/GeneralStrategy → Comparison is null/empty ──

    [Fact]
    public async Task GetDashboard_ClientWithNoPlanData_ComparisonIsNull()
    {
        var analysis = BuildAnalyzedAnalysis(OrgId, ClientId, hasStats: true, hasAi: true);
        var harness = new DashboardHarness(analysis);
        // No plan or strategy registered for this client

        var result = await harness.Handler.Handle(
            new GetProfileDashboardQuery(analysis.Id, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // When client has no plan data, comparison should be null (PA-17: omit panel)
        Assert.Null(result.Value.Comparison);
    }

    // ── Not found / cross-org guard ───────────────────────────────────────────

    [Fact]
    public async Task GetDashboard_NonExistentId_ReturnsNotFound()
    {
        var harness = new DashboardHarness(analysis: null);

        var result = await harness.Handler.Handle(
            new GetProfileDashboardQuery(Guid.NewGuid(), OrgId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetDashboard_CrossOrgId_ReturnsNotFound()
    {
        var analysis = BuildAnalyzedAnalysis(OrgId, ClientId, hasStats: true, hasAi: true);
        var harness = new DashboardHarness(analysis);
        var otherOrg = Guid.NewGuid();

        var result = await harness.Handler.Handle(
            new GetProfileDashboardQuery(analysis.Id, otherOrg),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.not_found", result.Error.Code);
    }

    // ── Raw posts returned for client-side dynamism (PA-16) ──────────────────

    [Fact]
    public async Task GetDashboard_AnalyzedRecord_IncludesRawOwnerPosts()
    {
        var analysis = BuildAnalyzedAnalysis(OrgId, ClientId, hasStats: true, hasAi: true);
        var harness = new DashboardHarness(analysis);

        var result = await harness.Handler.Handle(
            new GetProfileDashboardQuery(analysis.Id, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.OwnerPosts);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DomainProfileAnalysis BuildAnalyzedAnalysis(
        Guid orgId,
        Guid? clientId,
        bool hasStats,
        bool hasAi,
        bool medianViewsNull = false)
    {
        DomainProfileAnalysis analysis;
        if (clientId.HasValue)
            analysis = DomainProfileAnalysis.CreateFromClient(
                orgId, clientId.Value, Guid.NewGuid(), "testbrand", Now);
        else
            analysis = DomainProfileAnalysis.CreateFromUrl(orgId, "https://instagram.com/testbrand", Now);

        analysis.StartScraping(Now);
        analysis.CompleteScrape(isPartial: false, Now);
        analysis.StartAnalyzing(Now);

        var aiAnalysis = new ProfileAiAnalysis(
            "Test summary",
            "Marketers 25-35",
            ["Pillar A"],
            ["Video"],
            ["Mon", "Wed"],
            [new DetectedBrandColor("Blue", "#0000FF")],
            ["Arial"],
            ["https://img.example.com/bg1.jpg"],
            "Professional",
            ["Engagement rate"],
            "Grow to 10k followers",
            "Post 3x per week",
            ["Good engagement"],
            ["Low reels"],
            ["Stories potential"],
            0.85,
            []);

        analysis.CompleteAnalysis(aiAnalysis, Now);

        // Set JSON data so the repository can deserialize it
        var stats = new ProfileStats(
            EngagementRate: 4.5,
            MedianViews: medianViewsNull ? null : 1200.0,
            AvgLikesByWeekday: new Dictionary<int, WeekdayMetric>
                { [0] = new WeekdayMetric(50, 5, null) },
            AvgCommentsByWeekday: new Dictionary<int, WeekdayMetric>
                { [0] = new WeekdayMetric(50, 5, null) },
            AvgViewsByWeekday: new Dictionary<int, WeekdayMetric>
                { [0] = new WeekdayMetric(50, 5, 600) },
            ContentTypeMix: new Dictionary<string, double> { ["Video"] = 0.6, ["Image"] = 0.4 },
            PostingCadenceDays: 2.5,
            OwnerPostCount: 20,
            DegradedPostCount: 0);

        var rawPostsJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new
            {
                PostId = "post1",
                MediaType = "Video",
                LikesCount = 100L,
                CommentsCount = 10L,
                VideoViewCount = (long?)1200,
                Timestamp = "2026-05-01T10:00:00Z",
                IsPaidPartnership = false,
                Hashtags = new string[] { "#test" }
            }
        });

        var statsJson = System.Text.Json.JsonSerializer.Serialize(stats);
        var diagnosticsJson = "{}";
        analysis.SetRawData(rawPostsJson, statsJson, diagnosticsJson);

        var aiJson = System.Text.Json.JsonSerializer.Serialize(aiAnalysis);
        analysis.SetAiAnalysis(aiJson, diagnosticsJson);

        return analysis;
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private sealed class DashboardHarness
    {
        public DashboardHarness(DomainProfileAnalysis? analysis)
        {
            Repository = new FakeDashboardAnalysisRepository(analysis);
            ContentPlanRepo = new FakeContentPlanRepository();
            GeneralStrategyRepo = new FakeGeneralStrategyRepository();

            Handler = new GetProfileDashboardQueryHandler(
                Repository,
                ContentPlanRepo,
                GeneralStrategyRepo);
        }

        public FakeDashboardAnalysisRepository Repository { get; }
        public FakeContentPlanRepository ContentPlanRepo { get; }
        public FakeGeneralStrategyRepository GeneralStrategyRepo { get; }
        public GetProfileDashboardQueryHandler Handler { get; }
    }

    private sealed class FakeDashboardAnalysisRepository : IProfileAnalysisRepository
    {
        private readonly DomainProfileAnalysis? _analysis;

        public FakeDashboardAnalysisRepository(DomainProfileAnalysis? analysis)
            => _analysis = analysis;

        public Task<DomainProfileAnalysis?> GetByIdAsync(
            Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _analysis?.Id == analysisId && _analysis.OrganizationId == organizationId
                    ? _analysis : null);

        public Task AddAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(
            Guid organizationId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileAnalysisHistoryPage([], 0, page, pageSize));

        public Task UpdateAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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

    private sealed class FakeContentPlanRepository : IContentPlanRepository
    {
        private ContentPlan? _plan;
        public bool WasQueried { get; private set; }

        public void AddPlan(Guid clientId, Guid orgId,
            string objectives, IReadOnlyList<string> kpis, string? strategyText)
        {
            var period = Period.Monthly(DateTime.UtcNow.Year, DateTime.UtcNow.Month).Value;
            var planResult = ContentPlan.Create(
                ContentPlanId.New(), clientId, orgId, period,
                objectives, kpis, DateTime.UtcNow);
            _plan = planResult.Value;
        }

        public Task<ContentPlan?> GetByClientAndPeriodAsync(
            Guid organizationId, Guid clientId, Period period, CancellationToken ct)
        {
            WasQueried = true;
            return Task.FromResult(_plan?.ClientId == clientId ? _plan : null);
        }

        public Task<IReadOnlyList<ContentPlan>> ListByClientAsync(
            Guid organizationId, Guid clientId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentPlan>>(
                _plan?.ClientId == clientId ? new[] { _plan } : []);

        public Task UpsertAsync(ContentPlan plan, CancellationToken ct) => Task.CompletedTask;

        public Task LinkSlotPackageAsync(
            Guid planId, CalendarSlotId slotId, Guid packageId, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeGeneralStrategyRepository : IGeneralStrategyRepository
    {
        private DomainGeneralStrategy? _strategy;
        public bool WasQueried { get; private set; }

        public void AddStrategy(Guid clientId, Guid orgId,
            IReadOnlyList<string> pillars,
            string? audience, string? postingFrequency)
        {
            var result = DomainGeneralStrategy.Create(
                DomainGeneralStrategyId.New(), clientId, orgId,
                pillars, audience, postingFrequency, notes: null, DateTime.UtcNow);
            _strategy = result.Value;
        }

        public Task<DomainGeneralStrategy?> GetByClientIdAsync(
            Guid clientId, Guid organizationId, CancellationToken ct)
        {
            WasQueried = true;
            return Task.FromResult(
                _strategy?.ClientId == clientId ? _strategy : null);
        }

        public void Add(DomainGeneralStrategy strategy) { }
        public void Update(DomainGeneralStrategy strategy) { }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
