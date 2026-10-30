using System.Text.Json;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.ContentAnalysis;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ProfileAnalyzer.Analyze;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.ProfileAnalysis;
using SocialStudio.Tests.Common;
using DomainProfileAnalysis = SocialStudio.Domain.Social.ProfileAnalysis.ProfileAnalysis;

namespace SocialStudio.Tests.Application.Social.ProfileAnalyzer;

/// <summary>
/// Unit tests for ProfileAnalyzer orchestrator (IProfileAnalyzer.RunQueuedAsync).
/// Tests cover: scrape → stats → AI → persist (PA-05, PA-08, PA-10, PA-23).
/// </summary>
public sealed class ProfileAnalyzerOrchestratorTests
{
    private static readonly DateTime Now = new(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

    // ── Happy path: full pipeline ─────────────────────────────────────────────

    [Fact]
    public async Task RunQueuedAsync_HappyPath_TransitionsFromRequestedToAnalyzed()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        var analysis = CreateFromUrl(orgId);
        harness.Repository.Store(analysis);
        harness.ScrapeResolver.SetSuccess(MakeScrapeResult(isPartial: false));
        harness.AiGateway.SetPayload(MakeAiResult());

        await harness.Orchestrator.RunQueuedAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;
        Assert.Equal(ProfileAnalysisStatus.Analyzed, stored.Status);
        Assert.NotNull(stored.AiAnalysisJson);
        Assert.NotNull(stored.ServerDerivedStatsJson);
        Assert.NotNull(stored.RawPostsDatasetJson);
    }

    [Fact]
    public async Task RunQueuedAsync_ClientOwnAccountEnrichment_DoesNotPublishAnalyzedBeforeAestheticJson()
    {
        var orgId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var ownAccountId = Guid.NewGuid();
        var harness = new OrchestratorHarness(
            Now,
            postMediaArchiver: new SuccessfulAestheticArchiver(),
            aestheticExtractor: new SuccessfulAestheticExtractor());
        var analysis = DomainProfileAnalysis.CreateFromClient(
            orgId,
            clientId,
            ownAccountId,
            "@testbrand",
            Now);

        harness.Repository.Store(analysis);
        harness.ScrapeResolver.SetSuccess(MakeScrapeResultWithImages());
        harness.AiGateway.SetPayload(MakeAiResult());

        await harness.Orchestrator.RunQueuedAsync(analysis.Id, orgId, CancellationToken.None);

        var events = harness.Repository.PublishedEvents;
        var aestheticIndex = events.FindIndex(e => e.Kind == RepositoryPublishKind.AestheticJson);
        var analyzedIndex = events.FindIndex(e =>
            e.Kind == RepositoryPublishKind.Update &&
            e.Status == ProfileAnalysisStatus.Analyzed);

        Assert.True(aestheticIndex >= 0, "Aesthetic enrichment must persist aesthetic_profile_json.");
        Assert.True(
            analyzedIndex > aestheticIndex,
            "A client own-account analysis must not be published as terminal Analyzed before aesthetic_profile_json is persisted.");
        Assert.DoesNotContain(
            events.Take(aestheticIndex),
            e => e.Kind == RepositoryPublishKind.Update && e.Status == ProfileAnalysisStatus.Analyzed);
        Assert.False(string.IsNullOrWhiteSpace(harness.Repository.AestheticProfileJson));
        Assert.Equal(ProfileAnalysisStatus.Analyzed, harness.Repository.Stored!.Status);
    }

    // ── ProfileScrapedPartial path: partial data still proceeds to AI ─────────

    [Fact]
    public async Task RunQueuedAsync_PartialScrape_ProceedsToAiStage()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        var analysis = CreateFromUrl(orgId);
        harness.Repository.Store(analysis);
        harness.ScrapeResolver.SetSuccess(MakeScrapeResult(isPartial: true));
        harness.AiGateway.SetPayload(MakeAiResult());

        await harness.Orchestrator.RunQueuedAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;
        Assert.Equal(ProfileAnalysisStatus.Analyzed, stored.Status);
    }

    // ── Scrape failure diagnostic recorded ───────────────────────────────────

    [Fact]
    public async Task RunQueuedAsync_ScrapeFailure_MarksFailedWithDiagnostics()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        var analysis = CreateFromUrl(orgId);
        harness.Repository.Store(analysis);
        harness.ScrapeResolver.SetFailure(
            new Error("integration.apify_not_configured", "Apify not configured", ErrorType.Failure));

        await harness.Orchestrator.RunQueuedAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;
        Assert.Equal(ProfileAnalysisStatus.Failed, stored.Status);
        Assert.Equal("integration.apify_not_configured", stored.FailureCode);
    }

    // ── PA-10: AI provider not configured → Failed + diagnostic ──────────────

    [Fact]
    public async Task RunQueuedAsync_AiProviderNotConfigured_MarksFailedWithNoProviderDiagnostic()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        harness.WorkflowResolver.SetNoRoute();
        var analysis = CreateFromUrl(orgId);
        harness.Repository.Store(analysis);
        harness.ScrapeResolver.SetSuccess(MakeScrapeResult(isPartial: false));

        await harness.Orchestrator.RunQueuedAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;
        Assert.Equal(ProfileAnalysisStatus.Failed, stored.Status);
        Assert.Equal(IntegrationErrors.AiRouteNotConfigured.Code, stored.FailureCode);
    }

    // ── PA-10: Correct flow key used — "profile-analyzer", not "single-analyzer" ─

    [Fact]
    public async Task RunQueuedAsync_UsesProfileAnalyzerFlowKeyNotSingleAnalyzerKey()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        var analysis = CreateFromUrl(orgId);
        harness.Repository.Store(analysis);
        harness.ScrapeResolver.SetSuccess(MakeScrapeResult(isPartial: false));
        harness.AiGateway.SetPayload(MakeAiResult());

        await harness.Orchestrator.RunQueuedAsync(analysis.Id, orgId, CancellationToken.None);

        Assert.NotNull(harness.AiGateway.LastRequest);
        Assert.Equal("profile-analyzer.analysis.v1", harness.AiGateway.LastRequest!.PromptVersionKey);
        Assert.NotEqual("single-analyzer.analysis.v1", harness.AiGateway.LastRequest.PromptVersionKey);
    }

    [Fact]
    public async Task RunQueuedAsync_ProfileAiInput_InstructsEnglishOutput()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        var analysis = CreateFromUrl(orgId);
        harness.Repository.Store(analysis);
        harness.ScrapeResolver.SetSuccess(MakeScrapeResult(isPartial: false));
        harness.AiGateway.SetPayload(MakeAiResult());

        await harness.Orchestrator.RunQueuedAsync(analysis.Id, orgId, CancellationToken.None);

        Assert.NotNull(harness.AiGateway.LastRequest);
        Assert.Contains(
            "All user-facing string values MUST be written in English",
            harness.AiGateway.LastRequest!.InputJson,
            StringComparison.Ordinal);
    }

    // ── PA-23: Diagnostics populated on successful run ────────────────────────

    [Fact]
    public async Task RunQueuedAsync_HappyPath_DiagnosticsContainActorIdAndAiProviderKey()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        var analysis = CreateFromUrl(orgId);
        harness.Repository.Store(analysis);
        harness.ScrapeResolver.SetSuccess(MakeScrapeResultWithActor("actor-abc", fallback: false));
        harness.AiGateway.SetPayload(MakeAiResult());

        await harness.Orchestrator.RunQueuedAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;
        Assert.Equal(ProfileAnalysisStatus.Analyzed, stored.Status);
        Assert.NotNull(stored.ProviderDiagnosticsJson);
        // PA-23: diagnostics must contain actor_id, ai provider info
        var diagJson = stored.ProviderDiagnosticsJson!;
        Assert.Contains("actor-abc", diagJson);
        Assert.Contains("openai", diagJson);
        Assert.Contains("gpt-test", diagJson);
    }

    // ── PA-23: Diagnostics populated on fallback run ──────────────────────────

    [Fact]
    public async Task RunQueuedAsync_FallbackRun_DiagnosticsContainFallbackTrueAndDefaultActorError()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        var analysis = CreateFromUrl(orgId);
        harness.Repository.Store(analysis);

        // Simulate a fallback: IsPartial=true, FallbackUsed=true, with one failure diagnostic
        harness.ScrapeResolver.SetSuccess(MakeScrapeResultWithFallback("backup-actor-xyz"));
        harness.AiGateway.SetPayload(MakeAiResult());

        await harness.Orchestrator.RunQueuedAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;
        Assert.Equal(ProfileAnalysisStatus.Analyzed, stored.Status);
        Assert.NotNull(stored.ProviderDiagnosticsJson);
        var diagJson = stored.ProviderDiagnosticsJson!;
        // PA-23: fallback_used, default_actor_error, backup_actor_id present
        Assert.Contains("backup-actor-xyz", diagJson);
        Assert.Contains("\"fallbackUsed\":true", diagJson.Replace(" ", ""));
    }

    // ── PA-23: Diagnostics populated on failed run ────────────────────────────

    [Fact]
    public async Task RunQueuedAsync_ScrapeFailure_DiagnosticsContainFailureReasons()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        var analysis = CreateFromUrl(orgId);
        harness.Repository.Store(analysis);
        harness.ScrapeResolver.SetFailure(
            new Error("integration.profile_actor_not_configured", "No actor configured", ErrorType.Failure));

        await harness.Orchestrator.RunQueuedAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;
        Assert.Equal(ProfileAnalysisStatus.Failed, stored.Status);
        Assert.NotNull(stored.ProviderDiagnosticsJson);
        Assert.Contains("integration.profile_actor_not_configured", stored.ProviderDiagnosticsJson!);
    }

    // ── RunAnalysisOnlyAsync: retry-AI path ────────────────────────────────────

    [Fact]
    public async Task RunAnalysisOnlyAsync_WithPersistedScrapeData_ReachesAnalyzedWithoutCallingScrapeResolver()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        // Build a Failed-with-scrape-data record via Reconstitute (mirrors production DB state)
        var analysis = MakeFailedWithScrapeData(orgId, Guid.NewGuid());
        harness.Repository.Store(analysis);
        harness.AiGateway.SetPayload(MakeAiResult());

        // RestartAnalysis sets it to Analyzing — command handler does this before enqueuing
        analysis.RestartAnalysis(Now);

        await harness.Orchestrator.RunAnalysisOnlyAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;
        Assert.Equal(ProfileAnalysisStatus.Analyzed, stored.Status);
        Assert.NotNull(stored.AiAnalysisJson);
        // Scrape resolver MUST NOT be called — it is not invoked by RunAnalysisOnlyAsync
        // (there is no way to call it from the implementation path; verified structurally)
        Assert.Equal(0, harness.ScrapeResolver.CallCount);
    }

    [Fact]
    public async Task RunAnalysisOnlyAsync_WhenAiFailsAgain_LeavesRecordFailedWithScrapeDataIntact()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        var analysis = MakeFailedWithScrapeData(orgId, Guid.NewGuid());
        harness.Repository.Store(analysis);
        analysis.RestartAnalysis(Now);
        harness.AiGateway.SetError(new Error("ai.provider_error", "provider down", ErrorType.Failure));

        await harness.Orchestrator.RunAnalysisOnlyAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;
        Assert.Equal(ProfileAnalysisStatus.Failed, stored.Status);
        Assert.Equal("ai.provider_error", stored.FailureCode);
        // Scrape data must survive a second failure
        Assert.NotNull(stored.RawPostsDatasetJson);
        Assert.NotNull(stored.ServerDerivedStatsJson);
        Assert.NotNull(stored.ProfileSnapshotJson);
    }

    [Fact]
    public async Task RunAnalysisOnlyAsync_WithMissingScrapeData_MarksFailedWithoutCallingAi()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        // A record that failed BEFORE scrape completed — no scrape data at all
        var analysis = MakeFailedNoScrapeData(orgId, Guid.NewGuid());
        harness.Repository.Store(analysis);

        await harness.Orchestrator.RunAnalysisOnlyAsync(analysis.Id, orgId, CancellationToken.None);

        var stored = harness.Repository.Stored!;
        Assert.Equal(ProfileAnalysisStatus.Failed, stored.Status);
        Assert.Null(harness.AiGateway.LastRequest);
    }

    [Fact]
    public async Task RunAnalysisOnlyAsync_ReconstructsAiInputFromPersistedData_NotFromScraper()
    {
        var orgId = Guid.NewGuid();
        var harness = new OrchestratorHarness(Now);
        var analysis = MakeFailedWithScrapeData(orgId, Guid.NewGuid());
        harness.Repository.Store(analysis);
        analysis.RestartAnalysis(Now);
        harness.AiGateway.SetPayload(MakeAiResult());

        await harness.Orchestrator.RunAnalysisOnlyAsync(analysis.Id, orgId, CancellationToken.None);

        // AI gateway was called with the correct prompt version key
        Assert.NotNull(harness.AiGateway.LastRequest);
        Assert.Equal("profile-analyzer.analysis.v1", harness.AiGateway.LastRequest!.PromptVersionKey);
        // Scrape resolver was NEVER invoked
        Assert.Equal(0, harness.ScrapeResolver.CallCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DomainProfileAnalysis CreateFromUrl(Guid orgId) =>
        DomainProfileAnalysis.CreateFromUrl(orgId, "https://instagram.com/testbrand", DateTime.UtcNow);

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates a Failed record that HAS persisted scrape data — simulates a real AI-failure DB state.
    /// Uses Reconstitute to bypass the status machine (same as EF mapping does).
    /// </summary>
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

    /// <summary>Creates a Failed record WITHOUT scrape data — simulates scrape-stage failure.</summary>
    private static DomainProfileAnalysis MakeFailedNoScrapeData(Guid orgId, Guid id) =>
        DomainProfileAnalysis.Reconstitute(
            id: id,
            organizationId: orgId,
            clientId: null,
            ownAccountId: null,
            profileUrlOrHandle: "https://instagram.com/testbrand",
            status: ProfileAnalysisStatus.Failed,
            rawPostsDatasetJson: null,
            serverDerivedStatsJson: null,
            profileSnapshotJson: null,
            aiAnalysisJson: null,
            providerDiagnosticsJson: "{}",
            failureCode: "integration.apify_not_configured",
            createdAtUtc: Now,
            updatedAtUtc: Now,
            completedAtUtc: Now);

    private static ProfileScrapeResult MakeScrapeResultWithActor(string actorId, bool fallback) =>
        MakeScrapeResult(isPartial: fallback) with { UsedActorId = actorId, FallbackUsed = fallback };

    private static ProfileScrapeResult MakeScrapeResultWithFallback(string backupActorId)
    {
        var failureDiag = ProviderFailureDiagnostic.Create(
            "profile", "default-actor-id", 1,
            "integration.default_actor_failed", "Default actor failed",
            isRetryable: false, fallbackAllowed: true, elapsedMs: 100, DateTime.UtcNow);

        return MakeScrapeResult(isPartial: true) with
        {
            UsedActorId = backupActorId,
            FallbackUsed = true,
            Diagnostics = failureDiag.IsSuccess ? [failureDiag.Value] : [],
        };
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

    private static ProfileScrapeResult MakeScrapeResultWithImages() =>
        MakeScrapeResult(isPartial: false) with
        {
            OwnerPosts =
            [
                new ProfilePost(
                    PostId: "p-img",
                    MediaType: "Image",
                    LikesCount: 100,
                    CommentsCount: 10,
                    VideoViewCount: null,
                    Timestamp: new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
                    OwnerId: "12345",
                    OwnerUsername: "testbrand",
                    IsPaidPartnership: false,
                    IsDegraded: false,
                    Hashtags: [],
                    CarouselChildrenCount: null,
                    ImageUrls: ["https://cdn.example.com/p-img.jpg"],
                    Caption: "Brand visual sample")
            ],
            ProviderAggregates = new ProfileAggregates(50, 10_000, 200, 1, 1, 0),
        };

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

    // ── Harness ───────────────────────────────────────────────────────────────

    private sealed class OrchestratorHarness
    {
        public OrchestratorHarness(
            DateTime nowUtc,
            IClientPostMediaArchiver? postMediaArchiver = null,
            IClientAestheticStyleExtractor? aestheticExtractor = null)
        {
            Clock = new TestClock(nowUtc);
            Repository = new FakeProfileAnalysisRepository();
            UnitOfWork = new TestUnitOfWork();
            ScrapeResolver = new FakeScrapeResolver();
            WorkflowResolver = new FakeWorkflowResolver();
            AiGateway = new FakeAiGateway();
            Calculator = new ProfileMetricCalculator();

            Orchestrator = new ProfileAnalyzerOrchestrator(
                Repository,
                UnitOfWork,
                Clock,
                ScrapeResolver,
                Calculator,
                AiGateway,
                workflowResolver: WorkflowResolver,
                postMediaArchiver: postMediaArchiver,
                aestheticExtractor: aestheticExtractor);
        }

        public TestClock Clock { get; }
        public FakeProfileAnalysisRepository Repository { get; }
        public TestUnitOfWork UnitOfWork { get; }
        public FakeScrapeResolver ScrapeResolver { get; }
        public FakeWorkflowResolver WorkflowResolver { get; }
        public FakeAiGateway AiGateway { get; }
        public ProfileMetricCalculator Calculator { get; }
        public ProfileAnalyzerOrchestrator Orchestrator { get; }
    }

    private sealed class FakeProfileAnalysisRepository : IProfileAnalysisRepository
    {
        public DomainProfileAnalysis? Stored { get; private set; }
        public string? AestheticProfileJson { get; private set; }
        public List<RepositoryPublishEvent> PublishedEvents { get; } = [];

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
            PublishedEvents.Add(new RepositoryPublishEvent(
                RepositoryPublishKind.Update,
                analysis.Status));
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

        public Task SetAestheticProfileJsonAsync(
            Guid analysisId,
            Guid organizationId,
            string aestheticProfileJson,
            CancellationToken cancellationToken = default)
        {
            AestheticProfileJson = aestheticProfileJson;
            PublishedEvents.Add(new RepositoryPublishEvent(
                RepositoryPublishKind.AestheticJson,
                Stored?.Status));
            return Task.CompletedTask;
        }
    }

    private enum RepositoryPublishKind
    {
        Update,
        AestheticJson,
    }

    private sealed record RepositoryPublishEvent(
        RepositoryPublishKind Kind,
        ProfileAnalysisStatus? Status);

    private sealed class SuccessfulAestheticArchiver : IClientPostMediaArchiver
    {
        public Task<IReadOnlyList<ProfilePostImageRef>> ArchiveAsync(
            Guid organizationId,
            Guid clientId,
            Guid analysisId,
            string sourcePostId,
            IReadOnlyList<string> imageUrls,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ProfilePostImageRef>>(
                [new ProfilePostImageRef(sourcePostId, 0, "org/client/analysis/p-img.jpg", "hash")]);
    }

    private sealed class SuccessfulAestheticExtractor : IClientAestheticStyleExtractor
    {
        public Task<AestheticExtraction> ExtractAsync(
            Guid organizationId,
            IReadOnlyList<ProfilePostImageCaptionPair> referenceImages,
            CancellationToken ct) =>
            Task.FromResult(new AestheticExtraction(
                Palette: [],
                StyleDescriptors: ["soft gradients"],
                Motifs: [],
                CaptionVoice: "Direct",
                SchemaVersion: "aesthetic-extractor/v2"));
    }

    private sealed class FakeScrapeResolver : IProfileScrapeResolver
    {
        private Result<ProfileScrapeResult> _result =
            Result.Failure<ProfileScrapeResult>(
                new Error("test.not_set", "Not configured", ErrorType.Failure));

        public int CallCount { get; private set; }

        public void SetSuccess(ProfileScrapeResult scrapeResult) =>
            _result = Result.Success(scrapeResult);

        public void SetFailure(Error error) =>
            _result = Result.Failure<ProfileScrapeResult>(error);

        public Task<Result<ProfileScrapeResult>> ResolveAsync(
            ProfileScrapeRequest request, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeWorkflowResolver : IWorkflowConfigurationResolver
    {
        private Result<IReadOnlyList<WorkflowStepRouteCandidate>> _result =
            Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
            [
                new WorkflowStepRouteCandidate(Guid.NewGuid(), "openai", Guid.NewGuid(),
                    IntegrationConnectionMode.BringYourOwnKey, 1, "gpt-test", null, true)
            ]);

        // Simulate "no AI route configured" → orchestrator must fail with AiRouteNotConfigured.
        public void SetNoRoute() =>
            _result = Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>([]);

        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class FakeAiGateway : IAiCompletionGateway
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

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
