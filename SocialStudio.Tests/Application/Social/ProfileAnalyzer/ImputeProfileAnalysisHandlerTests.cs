using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.ClientBrandManuals.Dtos;
using SocialStudio.Application.Features.Social.ProfileAnalyzer.Analyze;
using SocialStudio.Application.Features.Social.ProfileAnalyzer.Impute;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.ContentPlans;
using SocialStudio.Domain.Social.ProfileAnalysis;
using DomainProfileAnalysis = SocialStudio.Domain.Social.ProfileAnalysis.ProfileAnalysis;
using DomainGeneralStrategy = SocialStudio.Domain.Social.GeneralStrategies.GeneralStrategy;
using DomainGeneralStrategyId = SocialStudio.Domain.Social.GeneralStrategies.GeneralStrategyId;

namespace SocialStudio.Tests.Application.Social.ProfileAnalyzer;

/// <summary>
/// Unit tests for ImputeProfileAnalysisHandler (PA-20, PA-21, PA-22).
/// </summary>
public sealed class ImputeProfileAnalysisHandlerTests
{
    private static readonly DateTime Now = new(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    // ── PA-22: Impute on non-Analyzed status → 422 error ─────────────────────

    [Fact]
    public async Task Handle_NonAnalyzedStatus_ReturnsNotAnalyzedError()
    {
        // Analysis is in Requested status (not Analyzed)
        var analysis = DomainProfileAnalysis.CreateFromClient(
            OrgId, ClientId, Guid.NewGuid(), "testbrand", Now);
        // No further status transitions → stays at Requested
        var harness = new ImputeHarness(analysis);

        var result = await harness.Handler.Handle(
            new ImputeProfileAnalysisCommand(
                analysis.Id, OrgId,
                [ProfileImputeTarget.ColorPalette]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.not_analyzed", result.Error.Code);
    }

    // ── PA-20: ClientId null (URL path) → impute blocked ──────────────────────

    [Fact]
    public async Task Handle_UrlPathAnalysis_ReturnsClientRequiredError()
    {
        var analysis = BuildAnalyzedUrlPathAnalysis(OrgId);
        var harness = new ImputeHarness(analysis);

        var result = await harness.Handler.Handle(
            new ImputeProfileAnalysisCommand(
                analysis.Id, OrgId,
                [ProfileImputeTarget.ColorPalette]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.profile_analysis.client_required", result.Error.Code);
    }

    // ── PA-20: Only selected BrandManual fields dispatched ────────────────────

    [Fact]
    public async Task Handle_BrandManualTargets_DispatchesUpdateWithSelectedFields()
    {
        var analysis = BuildAnalyzedClientAnalysis(OrgId, ClientId);
        var harness = new ImputeHarness(analysis);
        harness.AddBrandManual(ClientId, OrgId);

        var result = await harness.Handler.Handle(
            new ImputeProfileAnalysisCommand(
                analysis.Id, OrgId,
                [ProfileImputeTarget.ColorPalette, ProfileImputeTarget.VoiceAndTone]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, harness.BrandManualRepo.UpdateCallCount);
        var updated = harness.BrandManualRepo.LastUpdated;
        Assert.NotNull(updated);
        // ColorPalette must be imputed from AI analysis detectedBrandColors
        Assert.NotEmpty(updated!.ColorPalette);
        // VoiceAndTone must be imputed from AI analysis voiceTone
        Assert.Equal("Professional", updated.VoiceAndTone);
    }

    // ── PA-20: Only selected ContentPlan fields dispatched ────────────────────

    [Fact]
    public async Task Handle_ContentPlanTargets_DispatchesUpsertWithSelectedFields()
    {
        var analysis = BuildAnalyzedClientAnalysis(OrgId, ClientId);
        var harness = new ImputeHarness(analysis);
        harness.AddContentPlan(ClientId, OrgId);

        var result = await harness.Handler.Handle(
            new ImputeProfileAnalysisCommand(
                analysis.Id, OrgId,
                [ProfileImputeTarget.ObjectiveText, ProfileImputeTarget.ObjectiveKpis]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(harness.ContentPlanRepo.UpsertWasCalled);
    }

    // ── PA-20: Only selected GeneralStrategy fields dispatched ───────────────

    [Fact]
    public async Task Handle_GeneralStrategyTargets_DispatchesUpsertWithSelectedFields()
    {
        var analysis = BuildAnalyzedClientAnalysis(OrgId, ClientId);
        var harness = new ImputeHarness(analysis);
        harness.AddGeneralStrategy(ClientId, OrgId);

        var result = await harness.Handler.Handle(
            new ImputeProfileAnalysisCommand(
                analysis.Id, OrgId,
                [ProfileImputeTarget.ContentPillars, ProfileImputeTarget.PostingFrequency]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(harness.GeneralStrategyRepo.UpsertWasCalled);
        // ContentPillars from AI must be set
        Assert.NotEmpty(harness.GeneralStrategyRepo.LastUpsertedContentPillars!);
        // PostingFrequency maps from postingDays[] → descriptive string
        Assert.NotNull(harness.GeneralStrategyRepo.LastUpsertedPostingFrequency);
    }

    // ── PA-21: Load-merge-write preserves unselected BrandManual fields ───────

    [Fact]
    public async Task Handle_BrandManualPartialImpute_PreservesUnselectedFields()
    {
        var analysis = BuildAnalyzedClientAnalysis(OrgId, ClientId);
        var harness = new ImputeHarness(analysis);
        // Add brand manual with existing Typography and Logos
        harness.AddBrandManual(ClientId, OrgId,
            existingTypographyName: "Helvetica",
            existingLogoName: "Main Logo");

        // Only impute ColorPalette — Typography and Logos must be preserved
        var result = await harness.Handler.Handle(
            new ImputeProfileAnalysisCommand(
                analysis.Id, OrgId,
                [ProfileImputeTarget.ColorPalette]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = harness.BrandManualRepo.LastUpdated;
        Assert.NotNull(updated);
        // Typography preserved (not in selected targets)
        Assert.Contains(updated!.Typography, t => t.Name == "Helvetica");
        // Logos preserved (not a valid impute target per spike findings — but still in the update)
        Assert.Contains(updated.Logos, l => l.Name == "Main Logo");
    }

    // ── PA-21: Re-impute idempotent — same final state ────────────────────────

    [Fact]
    public async Task Handle_ReImpute_SameFinalStateNoAccumulation()
    {
        var analysis = BuildAnalyzedClientAnalysis(OrgId, ClientId);
        var harness = new ImputeHarness(analysis);
        harness.AddBrandManual(ClientId, OrgId);

        var cmd = new ImputeProfileAnalysisCommand(
            analysis.Id, OrgId,
            [ProfileImputeTarget.ColorPalette]);

        // First impute
        var result1 = await harness.Handler.Handle(cmd, CancellationToken.None);
        Assert.True(result1.IsSuccess);
        var firstPalette = harness.BrandManualRepo.LastUpdated?.ColorPalette
            .Select(c => c.Hex).ToList();

        // Simulate re-impute by rebuilding harness with updated brand manual state
        // (in real flow the BrandManual has been persisted with new palette;
        //  second call loads the updated state and merges — same palette, no duplicates)
        var result2 = await harness.Handler.Handle(cmd, CancellationToken.None);
        Assert.True(result2.IsSuccess);
        var secondPalette = harness.BrandManualRepo.LastUpdated?.ColorPalette
            .Select(c => c.Hex).ToList();

        // Same hex values, same count — no accumulation
        Assert.Equal(firstPalette, secondPalette);
    }

    // ── PA-20: Partial selection — only selected targets touched ──────────────

    [Fact]
    public async Task Handle_PartialSelection_OnlySelectedTargetsTouched()
    {
        var analysis = BuildAnalyzedClientAnalysis(OrgId, ClientId);
        var harness = new ImputeHarness(analysis);
        harness.AddBrandManual(ClientId, OrgId);
        harness.AddGeneralStrategy(ClientId, OrgId);

        // Only select BrandManual targets — GeneralStrategy should NOT be touched
        var result = await harness.Handler.Handle(
            new ImputeProfileAnalysisCommand(
                analysis.Id, OrgId,
                [ProfileImputeTarget.ColorPalette, ProfileImputeTarget.Typography]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, harness.BrandManualRepo.UpdateCallCount);
        Assert.False(harness.GeneralStrategyRepo.UpsertWasCalled,
            "GeneralStrategy repo must NOT be called when no GeneralStrategy targets are selected.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DomainProfileAnalysis BuildAnalyzedClientAnalysis(Guid orgId, Guid clientId)
    {
        var analysis = DomainProfileAnalysis.CreateFromClient(
            orgId, clientId, Guid.NewGuid(), "testbrand", Now);
        analysis.StartScraping(Now);
        analysis.CompleteScrape(isPartial: false, Now);
        analysis.StartAnalyzing(Now);

        var aiAnalysis = new ProfileAiAnalysis(
            "Summary",
            "Marketers 25-35",
            ["Content pillar 1", "Content pillar 2"],
            ["Video"],
            ["Mon", "Wed", "Fri"],
            [new DetectedBrandColor("Blue", "#0000FF"), new DetectedBrandColor("White", "#FFFFFF")],
            ["Arial", "Roboto"],
            ["https://img.example.com/bg1.jpg"],
            "Professional",
            ["Engagement rate", "Reach"],
            "Grow to 10k followers",
            "Post 3x per week with mixed formats",
            ["Strong engagement"],
            ["Low reels usage"],
            ["Stories expansion"],
            0.9,
            []);

        analysis.CompleteAnalysis(aiAnalysis, Now);
        analysis.SetRawData("[]", "{}", "{}");

        // Serialize the actual AI analysis so the impute handler can deserialize it
        var aiJson = System.Text.Json.JsonSerializer.Serialize(
            aiAnalysis, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        analysis.SetAiAnalysis(aiJson, "{}");
        return analysis;
    }

    private static DomainProfileAnalysis BuildAnalyzedUrlPathAnalysis(Guid orgId)
    {
        var analysis = DomainProfileAnalysis.CreateFromUrl(
            orgId, "https://instagram.com/somebrand", Now);
        analysis.StartScraping(Now);
        analysis.CompleteScrape(isPartial: false, Now);
        analysis.StartAnalyzing(Now);

        var aiAnalysis = new ProfileAiAnalysis(
            "Summary", "Audience", ["Pillar"], ["Video"], ["Mon"],
            [], [], [], "Tone", [], "Objective", "Strategy",
            [], [], [], 0.8, []);

        analysis.CompleteAnalysis(aiAnalysis, Now);
        analysis.SetRawData("[]", "{}", "{}");

        var aiJson = System.Text.Json.JsonSerializer.Serialize(
            aiAnalysis, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        analysis.SetAiAnalysis(aiJson, "{}");
        return analysis;
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private sealed class ImputeHarness
    {
        public ImputeHarness(DomainProfileAnalysis analysis)
        {
            AnalysisRepo = new FakeImputeAnalysisRepository(analysis);
            BrandManualRepo = new FakeBrandManualRepository();
            ContentPlanRepo = new FakeImputeContentPlanRepository();
            GeneralStrategyRepo = new FakeImputeGeneralStrategyRepository();
            Clock = new FakeImputeClock(Now);

            Handler = new ImputeProfileAnalysisHandler(
                AnalysisRepo,
                BrandManualRepo,
                ContentPlanRepo,
                GeneralStrategyRepo,
                Clock);
        }

        public FakeImputeAnalysisRepository AnalysisRepo { get; }
        public FakeBrandManualRepository BrandManualRepo { get; }
        public FakeImputeContentPlanRepository ContentPlanRepo { get; }
        public FakeImputeGeneralStrategyRepository GeneralStrategyRepo { get; }
        public FakeImputeClock Clock { get; }
        public ImputeProfileAnalysisHandler Handler { get; }

        public void AddBrandManual(
            Guid clientId, Guid orgId,
            string? existingTypographyName = null,
            string? existingLogoName = null)
            => BrandManualRepo.SetManual(clientId, orgId, existingTypographyName, existingLogoName);

        public void AddContentPlan(Guid clientId, Guid orgId)
            => ContentPlanRepo.SetPlan(clientId, orgId, "Existing objective", [], null);

        public void AddGeneralStrategy(Guid clientId, Guid orgId)
            => GeneralStrategyRepo.SetStrategy(clientId, orgId, ["Existing pillar"], null, null);
    }

    private sealed class FakeImputeAnalysisRepository : IProfileAnalysisRepository
    {
        private DomainProfileAnalysis _analysis;

        public FakeImputeAnalysisRepository(DomainProfileAnalysis analysis)
            => _analysis = analysis;

        public Task<DomainProfileAnalysis?> GetByIdAsync(
            Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _analysis.Id == analysisId && _analysis.OrganizationId == organizationId
                    ? _analysis : (DomainProfileAnalysis?)null);

        public Task AddAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(
            Guid organizationId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileAnalysisHistoryPage([], 0, page, pageSize));

        public Task UpdateAsync(DomainProfileAnalysis analysis, CancellationToken cancellationToken = default)
        {
            _analysis = analysis;
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

    private sealed class FakeBrandManualRepository : IClientBrandManualRepository
    {
        private ClientBrandManual? _manual;
        public int UpdateCallCount { get; private set; }
        public ClientBrandManual? LastUpdated { get; private set; }

        public void SetManual(
            Guid clientId, Guid orgId,
            string? existingTypographyName = null,
            string? existingLogoName = null)
        {
            var typography = existingTypographyName is not null
                ? (IReadOnlyList<TypographyEntry>)
                    [TypographyEntry.Create(existingTypographyName, "Helvetica Neue", null, null, null).Value]
                : [];

            var logos = existingLogoName is not null
                ? (IReadOnlyList<LogoEntry>)
                    [LogoEntry.Create(existingLogoName, "https://example.com/logo.png", "primary").Value]
                : [];

            // Must provide at least one signal field so Create passes HasSomeSignal check
            // Use a notes fallback when nothing else is set
            var notes = (typography.Count == 0 && logos.Count == 0) ? "Initial brand manual" : null;

            var result = ClientBrandManual.Create(
                ClientBrandManualId.New(),
                new ClientId(clientId),
                orgId,
                "Brand Manual",
                [], logos, [], typography,
                voiceAndTone: null, notes: notes,
                DateTime.UtcNow);
            _manual = result.Value;
        }

        public Task<ClientBrandManual?> GetByIdAsync(
            ClientBrandManualId id, Guid organizationId, CancellationToken ct) =>
            Task.FromResult(_manual?.Id == id ? _manual : null);

        public Task<ClientBrandManual?> GetActiveByClientAsync(
            ClientId clientId, Guid organizationId, CancellationToken ct) =>
            Task.FromResult(_manual?.ClientId == clientId ? _manual : null);

        public Task<(IReadOnlyList<ClientBrandManual> Items, int TotalCount)> ListByClientAsync(
            ClientId clientId, Guid organizationId, bool includeInactive,
            int page, int pageSize, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<ClientBrandManual>, int)>(([], 0));

        public Task AddAsync(ClientBrandManual manual, CancellationToken ct) => Task.CompletedTask;

        public void Update(ClientBrandManual manual)
        {
            UpdateCallCount++;
            LastUpdated = manual;
            _manual = manual;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeImputeContentPlanRepository : IContentPlanRepository
    {
        private ContentPlan? _plan;
        public bool UpsertWasCalled { get; private set; }
        public ContentPlan? LastUpserted { get; private set; }

        public void SetPlan(Guid clientId, Guid orgId,
            string objectives, IReadOnlyList<string> kpis, string? strategyText)
        {
            var period = Period.Monthly(DateTime.UtcNow.Year, DateTime.UtcNow.Month).Value;
            var result = ContentPlan.Create(
                ContentPlanId.New(), clientId, orgId, period,
                objectives, kpis, DateTime.UtcNow);
            _plan = result.Value;
        }

        public Task<ContentPlan?> GetByClientAndPeriodAsync(
            Guid organizationId, Guid clientId, Period period, CancellationToken ct) =>
            Task.FromResult(_plan?.ClientId == clientId ? _plan : null);

        public Task<IReadOnlyList<ContentPlan>> ListByClientAsync(
            Guid organizationId, Guid clientId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentPlan>>(
                _plan?.ClientId == clientId ? new[] { _plan } : []);

        public Task UpsertAsync(ContentPlan plan, CancellationToken ct)
        {
            UpsertWasCalled = true;
            LastUpserted = plan;
            _plan = plan;
            return Task.CompletedTask;
        }

        public Task LinkSlotPackageAsync(
            Guid planId, CalendarSlotId slotId, Guid packageId, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeImputeGeneralStrategyRepository : IGeneralStrategyRepository
    {
        private DomainGeneralStrategy? _strategy;
        public bool UpsertWasCalled { get; private set; }
        public IReadOnlyList<string>? LastUpsertedContentPillars { get; private set; }
        public string? LastUpsertedPostingFrequency { get; private set; }

        public void SetStrategy(Guid clientId, Guid orgId,
            IReadOnlyList<string> pillars,
            string? audience, string? postingFrequency)
        {
            var result = DomainGeneralStrategy.Create(
                DomainGeneralStrategyId.New(), clientId, orgId,
                pillars, audience, postingFrequency, notes: null, DateTime.UtcNow);
            _strategy = result.Value;
        }

        public Task<DomainGeneralStrategy?> GetByClientIdAsync(
            Guid clientId, Guid organizationId, CancellationToken ct) =>
            Task.FromResult(_strategy?.ClientId == clientId ? _strategy : null);

        public void Add(DomainGeneralStrategy strategy)
        {
            UpsertWasCalled = true;
            _strategy = strategy;
            LastUpsertedContentPillars = strategy.ContentPillars;
            LastUpsertedPostingFrequency = strategy.PostingFrequency;
        }

        public void Update(DomainGeneralStrategy strategy)
        {
            UpsertWasCalled = true;
            _strategy = strategy;
            LastUpsertedContentPillars = strategy.ContentPillars;
            LastUpsertedPostingFrequency = strategy.PostingFrequency;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeImputeClock : IClock
    {
        private readonly DateTime _now;
        public FakeImputeClock(DateTime now) => _now = now;
        public DateTime UtcNow => _now;
    }
}
