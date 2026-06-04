using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Infrastructure.Observability;
using Incoders.Hive.Tests.Common;
using DomainProfileAnalysis = Incoders.Hive.Domain.Social.ProfileAnalysis.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ProfileAnalyzer;

/// <summary>
/// Regression tests that guard against the ProviderCallContext.Current is null bug.
///
/// Context: the AiCompletionGatewayInstrumentationDecorator throws
/// InvalidOperationException when ProviderCallContext.Current is null.
/// ProfileAnalyzerOrchestrator must call ProviderCallContext.Begin() before
/// invoking the gateway, or the AI stage crashes with "unexpected_error" instead
/// of reaching Analyzed.
///
/// These tests wire the REAL decorator (not a bare mock) so they would fail if
/// the Begin() call is removed from RunAiStagesAsync.
/// </summary>
public sealed class ProfileAnalyzerOrchestratorInstrumentationTests
{
    private static readonly DateTime Now = new(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

    // ─────────────────────────────────────────────────────────────────────────
    // Regression: RunQueuedAsync must establish ProviderCallContext before
    // calling the instrumented gateway. Without Begin(), the decorator throws
    // and the record lands in Failed (unexpected_error) instead of Analyzed.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunQueuedAsync_WithInstrumentedDecorator_ReachesAnalyzedStatus()
    {
        var orgId = Guid.NewGuid();
        var harness = new InstrumentedOrchestratorHarness(Now);
        var analysis = DomainProfileAnalysis.CreateFromUrl(orgId, "https://instagram.com/testbrand", DateTime.UtcNow);
        harness.Repository.Store(analysis);
        harness.ScrapeResolver.SetSuccess(MakeScrapeResult(isPartial: false));
        harness.InnerGateway.SetPayload(MakeAiResult());

        await harness.Orchestrator.RunQueuedAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;

        // If Begin() was missing, the decorator throws → catch block fires →
        // status becomes Failed with code "profile_analysis.unexpected_error".
        Assert.Equal(ProfileAnalysisStatus.Analyzed, stored.Status);
        Assert.NotNull(stored.AiAnalysisJson);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Regression: RunAnalysisOnlyAsync (retry-AI path) shares RunAiStagesAsync
    // and must also establish the context before the gateway call.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAnalysisOnlyAsync_WithInstrumentedDecorator_ReachesAnalyzedStatus()
    {
        var orgId = Guid.NewGuid();
        var harness = new InstrumentedOrchestratorHarness(Now);
        var analysis = MakeFailedWithScrapeData(orgId, Guid.NewGuid());
        harness.Repository.Store(analysis);
        analysis.RestartAnalysis(Now);
        harness.InnerGateway.SetPayload(MakeAiResult());

        await harness.Orchestrator.RunAnalysisOnlyAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;
        Assert.Equal(ProfileAnalysisStatus.Analyzed, stored.Status);
        Assert.NotNull(stored.AiAnalysisJson);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static DomainProfileAnalysis MakeFailedWithScrapeData(Guid orgId, Guid id)
    {
        var profile = new ProfileSnapshot("12345", "testbrand", 10_000, 50, 5, 3.5, 3.0, null, null, false, true, null);
        var posts = new List<ProfilePost>
        {
            new("p1", "Image", 100, 10, null, new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
                "12345", "testbrand", false, false, []),
        };
        var stats = new ProfileStats(3.5, 10000,
            new Dictionary<int, WeekdayMetric>(),
            new Dictionary<int, WeekdayMetric>(),
            new Dictionary<int, WeekdayMetric>(),
            new Dictionary<string, double> { ["Image"] = 1.0 },
            3.0, 1, 0);

        return DomainProfileAnalysis.Reconstitute(
            id: id,
            organizationId: orgId,
            clientId: null,
            ownAccountId: null,
            profileUrlOrHandle: "https://instagram.com/testbrand",
            status: ProfileAnalysisStatus.Failed,
            rawPostsDatasetJson: JsonSerializer.Serialize(posts, JsonOpts),
            serverDerivedStatsJson: JsonSerializer.Serialize(stats, JsonOpts),
            profileSnapshotJson: JsonSerializer.Serialize(profile, JsonOpts),
            aiAnalysisJson: null,
            providerDiagnosticsJson: "{}",
            failureCode: "ai.provider_error",
            createdAtUtc: Now,
            updatedAtUtc: Now,
            completedAtUtc: Now);
    }

    private static ProfileScrapeResult MakeScrapeResult(bool isPartial) =>
        new(
            Profile: new ProfileSnapshot(
                InstagramId: "12345",
                Username: "testbrand",
                FollowersCount: 10_000,
                PostsCount: 50,
                ReelsCount: 5,
                EngagementRate: 3.5,
                MedianDaysBetweenPosts: 3.0,
                ProfilePicUrl: null,
                Biography: null,
                IsVerified: false,
                IsBusinessAccount: true,
                BusinessCategoryName: null),
            OwnerPosts:
            [
                new ProfilePost("p1", "Image", 100, 10, null,
                    new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
                    "12345", "testbrand", false, false, []),
                new ProfilePost("p2", "Video", 200, 20, 5000,
                    new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
                    "12345", "testbrand", false, false, []),
            ],
            Reels: [],
            ProviderAggregates: new ProfileAggregates(50, 10_000, 200, 2, 2, 0),
            IsPartial: isPartial,
            Diagnostics: []);

    private static ProfileAnalysisResult MakeAiResult() =>
        new(
            Summary: "Great brand presence",
            AudienceProfile: "Young professionals",
            ContentPillars: ["Education", "Inspiration"],
            TopContentTypes: ["Reels"],
            PostingDays: ["Mon", "Wed"],
            DetectedBrandColors: [new ProfileAnalysisColorResult("Blue", "#0057FF")],
            DetectedFonts: ["Inter"],
            DetectedBackgroundImages: [],
            VoiceTone: "Authoritative",
            RecommendedKpis: ["Engagement Rate"],
            MonthlyObjectiveSuggestion: "Grow followers by 10%",
            ContentStrategySuggestion: "Post more reels",
            Strengths: ["Consistent posting"],
            Weaknesses: ["Low video views"],
            Opportunities: ["More hashtags"],
            Confidence: 0.85,
            Warnings: []);

    // ─────────────────────────────────────────────────────────────────────────
    // Harness
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wires the orchestrator with the REAL AiCompletionGatewayInstrumentationDecorator
    /// so any missing Begin() call surfaces as a test failure rather than being silently
    /// swallowed by a bare mock.
    /// </summary>
    private sealed class InstrumentedOrchestratorHarness
    {
        public InstrumentedOrchestratorHarness(DateTime nowUtc)
        {
            Repository = new FakeProfileAnalysisRepository2();
            ScrapeResolver = new FakeScrapeResolver2();
            DefaultAiSelector = new FakeDefaultAiSelector2();
            InnerGateway = new FakeInnerGateway();

            // Real ProviderCallContext — shared between the decorator and the orchestrator.
            // The orchestrator calls Begin() on this instance before each AI call;
            // the decorator reads Current from the same instance.
            ProviderCallContext = new ProviderCallContext();

            // Real decorator wrapping the stub inner gateway.
            // A no-op ledger is sufficient — we care about the context guard, not event capture.
            var ledger = new NoOpLedger();
            var instrumentedGateway = new AiCompletionGatewayInstrumentationDecorator(
                InnerGateway, ledger, ProviderCallContext);

            Orchestrator = new ProfileAnalyzerOrchestrator(
                Repository,
                new TestUnitOfWork2(),
                new TestClock(nowUtc),
                ScrapeResolver,
                new ProfileMetricCalculator(),
                instrumentedGateway,       // real decorator, NOT the bare stub
                DefaultAiSelector,
                logger: null,
                providerCallContext: ProviderCallContext);  // same instance
        }

        public FakeProfileAnalysisRepository2 Repository { get; }
        public FakeScrapeResolver2 ScrapeResolver { get; }
        public FakeDefaultAiSelector2 DefaultAiSelector { get; }
        public FakeInnerGateway InnerGateway { get; }
        public ProviderCallContext ProviderCallContext { get; }
        public ProfileAnalyzerOrchestrator Orchestrator { get; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test doubles (file-local, named with "2" suffix to avoid conflicts with
    // the nested types in the sibling test class in the same namespace)
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class FakeProfileAnalysisRepository2 : IProfileAnalysisRepository
    {
        public DomainProfileAnalysis? Stored { get; private set; }

        public void Store(DomainProfileAnalysis analysis) => Stored = analysis;

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
    }

    private sealed class FakeScrapeResolver2 : IProfileScrapeResolver
    {
        private Result<ProfileScrapeResult> _result =
            Result.Failure<ProfileScrapeResult>(
                new Error("test.not_set", "Not configured", ErrorType.Failure));

        public int CallCount { get; private set; }

        public void SetSuccess(ProfileScrapeResult result) => _result = Result.Success(result);
        public void SetFailure(Error error) => _result = Result.Failure<ProfileScrapeResult>(error);

        public Task<Result<ProfileScrapeResult>> ResolveAsync(
            ProfileScrapeRequest request, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeDefaultAiSelector2 : IDefaultAiSelector
    {
        private Result<DefaultAiSelection> _result =
            Result.Success(new DefaultAiSelection("openai", "gpt-test"));

        public void SetFailure(Error error) =>
            _result = Result.Failure<DefaultAiSelection>(error);

        public Task<Result<DefaultAiSelection>> SelectAsync(Guid organizationId, CancellationToken ct = default) =>
            Task.FromResult(_result);
    }

    /// <summary>
    /// Stub inner gateway — the instrumentation decorator wraps this.
    /// Unlike the bare FakeAiGateway in the sibling class, this one feeds the
    /// real decorator so the ProviderCallContext guard is exercised on every call.
    /// </summary>
    private sealed class FakeInnerGateway : IAiCompletionGateway
    {
        private ProfileAnalysisResult? _payload;
        private Error? _errorOverride;

        public AiJsonCompletionRequest? LastRequest { get; private set; }

        public void SetPayload(ProfileAnalysisResult payload) => _payload = payload;
        public void SetError(Error error) => _errorOverride = error;

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            LastRequest = request;

            if (_errorOverride is not null)
                return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(_errorOverride));

            var payload = (TResponse)(object)_payload!;
            return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(
                payload, "openai", "gpt-test", [])));
        }
    }

    private sealed class NoOpLedger : IUsageEventLedger
    {
        public Task RecordAsync(UsageEvent evt, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TestUnitOfWork2 : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
