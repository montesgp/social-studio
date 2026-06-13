using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Incoders.Hive.Tests.Common;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Tests.Application.Social.SingleAnalyzer;

/// <summary>
/// Tests that SingleContentAnalyzer resolves the AI provider/model SOLELY from the organization's
/// priority-ordered workflow configuration (no Default-AI selector). When no AI route is configured,
/// the analysis fails with AiRouteNotConfigured.
/// </summary>
public sealed class AnalyzeSingleContentDefaultAiTests
{
    private static readonly DateTime Now = new(2026, 5, 23, 10, 0, 0, DateTimeKind.Utc);

    // ─── Workflow-driven AI path ─────────────────────────────────────────────

    [Fact]
    public async Task Analyzer_UsesWorkflowConfiguredAiRoute_WhenConfigured()
    {
        var workflow = new ConfiguredWorkflowResolver()
            .With(
                "content-analysis",
                "analyze-content",
                "ai-analysis",
                null,
                [
                    new WorkflowStepRouteCandidate(
                        Guid.NewGuid(), "anthropic", Guid.NewGuid(),
                        IntegrationConnectionMode.BringYourOwnKey, 1, "claude-3-5-sonnet", null, true)
                ]);

        var harness = new DefaultAiHarness(Now, workflowResolver: workflow)
        {
            ContentResult = Result.Success(new SocialContentResolution(
                Snapshot("tiktok", []),
                []))
        };

        var result = await harness.Analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                Guid.NewGuid(), Guid.NewGuid(),
                "https://www.tiktok.com/@brand/video/123",
                ClientId: null,
                PreferredExtractionProviderKey: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : string.Empty);
        Assert.NotNull(harness.AiGateway.LastRequest);
        Assert.Equal("anthropic", harness.AiGateway.LastRequest!.PreferredProviderKey);
        Assert.Equal("claude-3-5-sonnet", harness.AiGateway.LastRequest!.PreferredModel);
    }

    [Fact]
    public async Task Analyzer_FailsWithAiRouteNotConfigured_WhenNoWorkflowRoute()
    {
        var harness = new DefaultAiHarness(Now)
        {
            ContentResult = Result.Success(new SocialContentResolution(
                Snapshot("tiktok", []),
                []))
        };

        var result = await harness.Analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                Guid.NewGuid(), Guid.NewGuid(),
                "https://www.tiktok.com/@brand/video/123",
                ClientId: null,
                PreferredExtractionProviderKey: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.AiRouteNotConfigured.Code, result.Error.Code);
    }

    // ─── Profile baseline (TASK 03 skipped test — now active) ────────────────

    [Fact]
    public async Task Handler_ReadsCapabilitiesFromProfile()
    {
        var spyProfile = new SpyProfile();
        var provider = new StaticProfileProvider(spyProfile);
        var harness = new DefaultAiHarness(Now, profileProvider: provider)
        {
            ContentResult = Result.Success(new SocialContentResolution(
                Snapshot("tiktok", []),
                []))
        };

        await harness.Analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                Guid.NewGuid(), Guid.NewGuid(),
                "https://www.tiktok.com/@brand/video/123",
                ClientId: null,
                PreferredExtractionProviderKey: null),
            CancellationToken.None);

        Assert.True(spyProfile.DetectPlatformCalled, "Profile.DetectPlatform should have been called");
    }

    [Fact]
    public async Task Analyzer_UsesWorkflowConfiguredAiModelAndScrapeActorFallbacks_WhenConfigured()
    {
        var organizationId = Guid.NewGuid();
        var workflow = new ConfiguredWorkflowResolver()
            .With(
                "content-analysis",
                "analyze-content",
                "ai-analysis",
                null,
                [
                    new WorkflowStepRouteCandidate(
                        Guid.NewGuid(),
                        "claude",
                        Guid.NewGuid(),
                        IntegrationConnectionMode.BringYourOwnKey,
                        Priority: 1,
                        ModelId: "claude-sonnet-synced",
                        ActorId: null,
                        IsEnabled: true)
                ])
            .With(
                "content-analysis",
                "scrape-video",
                "scrape-video",
                "tiktok",
                [
                    new WorkflowStepRouteCandidate(
                        Guid.NewGuid(),
                        "apify",
                        Guid.NewGuid(),
                        IntegrationConnectionMode.BringYourOwnKey,
                        Priority: 1,
                        ModelId: null,
                        ActorId: "actor-primary",
                        IsEnabled: true),
                    new WorkflowStepRouteCandidate(
                        Guid.NewGuid(),
                        "apify",
                        Guid.NewGuid(),
                        IntegrationConnectionMode.BringYourOwnKey,
                        Priority: 2,
                        ModelId: null,
                        ActorId: "actor-fallback",
                        IsEnabled: true)
                ]);

        var logger = new CapturingLogger<SingleContentAnalyzer>();
        var harness = new DefaultAiHarness(Now, workflowResolver: workflow, logger: logger)
        {
            ContentResult = Result.Success(new SocialContentResolution(
                Snapshot("tiktok", []),
                []))
        };

        var result = await harness.Analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                organizationId,
                Guid.NewGuid(),
                "https://www.tiktok.com/@brand/video/123",
                ClientId: null,
                PreferredExtractionProviderKey: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : string.Empty);
        Assert.NotNull(harness.AiGateway.LastRequest);
        Assert.Equal("claude", harness.AiGateway.LastRequest!.PreferredProviderKey);
        Assert.Equal("claude-sonnet-synced", harness.AiGateway.LastRequest!.PreferredModel);
        Assert.NotNull(harness.ContentResolver.LastRequest);
        Assert.Equal("apify", harness.ContentResolver.LastRequest!.PreferredProviderKey);
        Assert.Equal(["actor-primary", "actor-fallback"], harness.ContentResolver.LastRequest!.PreferredActorIds);
        Assert.Contains(logger.Messages, message => message.Contains("WorkflowStepResolved", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("WorkflowConfigurationResolved", StringComparison.Ordinal));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static SocialContentSnapshot Snapshot(
        string platform,
        IReadOnlyList<SocialContentWarning> warnings) =>
        new(platform, $"https://www.{platform}.com/@brand/video/123",
            "Hook example", "Caption", "@brand",
            "https://cdn.example.com/thumb.jpg", 38,
            new Dictionary<string, long> { ["views"] = 1_000 },
            Transcript: null, [], "provider://apify/runs/1", warnings, ["strategy"], "video", null);

    private static SingleAnalyzerAnalysis DefaultAnalysis() =>
        SingleAnalyzerAnalysis.Create(
            "summary", ["hook"], "audience", "structure",
            ["strength"], ["weakness"],
            [SingleAnalyzerIdea.Create("title", "concept", "rationale",
                SingleAnalyzerScore.Create(3, 3, 3, 3, 3, 3).Value).Value],
            ["rec"], 0.8, []).Value;

    // ─── Fakes ───────────────────────────────────────────────────────────────

    private sealed class SpyProfile : IContentAnalysisProfile
    {
        public bool DetectPlatformCalled { get; private set; }

        public string FlowKey => "single-analyzer";
        public string ExtractionCapabilityKey => "social-extraction";
        public string ExtractionProviderKey => "apify";
        public string AiCapabilityKey => "ai-analysis";
        public string PromptVersionKey => "single-analyzer.analysis.v1";

        public string DetectPlatform(string sourceUrl)
        {
            DetectPlatformCalled = true;
            return "tiktok";
        }
    }

    private sealed class StaticProfileProvider : IContentAnalysisProfileProvider
    {
        private readonly IContentAnalysisProfile _profile;
        public StaticProfileProvider(IContentAnalysisProfile profile) => _profile = profile;

        public Result<IContentAnalysisProfile> ForFlow(string flowKey) =>
            Result.Success(_profile);
    }

    private sealed class FakeSocialContentResolver : ISocialContentResolver
    {
        private readonly DefaultAiHarness _h;
        public FakeSocialContentResolver(DefaultAiHarness h) => _h = h;

        public SocialContentResolutionRequest? LastRequest { get; private set; }

        public Task<Result<SocialContentResolution>> ResolveAsync(
            SocialContentResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_h.ContentResult);
        }
    }

    private sealed class SpyAiGateway : IAiCompletionGateway
    {
        public AiJsonCompletionRequest? LastRequest { get; private set; }

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            LastRequest = request;
            var payload = (TResponse)(object)DefaultAnalysis();
            return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(
                payload, request.PreferredProviderKey ?? "openai", request.PreferredModel, [])));
        }
    }

    private sealed class DefaultAiHarness
    {
        public DefaultAiHarness(
            DateTime nowUtc,
            IContentAnalysisProfileProvider? profileProvider = null,
            IWorkflowConfigurationResolver? workflowResolver = null,
            ILogger<SingleContentAnalyzer>? logger = null)
        {
            Repository = new SimpleRepository();
            UnitOfWork = new SimpleUnitOfWork();
            Clock = new TestClock(nowUtc);
            ContentResolver = new FakeSocialContentResolver(this);
            AiGateway = new SpyAiGateway();

            var provider = profileProvider ?? new StaticProfileProvider(new SpyProfile());

            Analyzer = new SingleContentAnalyzer(
                Repository,
                UnitOfWork,
                Clock,
                ContentResolver,
                AiGateway,
                provider,
                workflowResolver: workflowResolver,
                logger: logger);
        }

        public Result<SocialContentResolution> ContentResult { get; set; } =
            Result.Failure<SocialContentResolution>(IntegrationErrors.ProviderRouteNotConfigured);

        public SimpleRepository Repository { get; }
        public SimpleUnitOfWork UnitOfWork { get; }
        public TestClock Clock { get; }
        public FakeSocialContentResolver ContentResolver { get; }
        public SpyAiGateway AiGateway { get; }
        public SingleContentAnalyzer Analyzer { get; }

        public sealed class SimpleRepository : ISingleContentAnalysisRepository
        {
            public SingleContentAnalysisRecord? StoredAnalysis { get; private set; }
            public List<SingleContentAnalysisStatus> StatusUpdates { get; } = [];

            public Task AddAsync(SingleContentAnalysisRecord a, CancellationToken ct = default)
            { StoredAnalysis = a; return Task.CompletedTask; }

            public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct = default) =>
                Task.FromResult<SingleContentAnalysisRecord?>(null);

            public Task UpdateStatusAsync(Guid id, Guid orgId, SingleContentAnalysisStatus status, DateTime updatedAt,
                CancellationToken ct = default, string? failureCode = null, string? failureKey = null)
            { StatusUpdates.Add(status); return Task.CompletedTask; }

            public Task UpdateAsync(SingleContentAnalysisRecord a, CancellationToken ct = default)
            { StoredAnalysis = a; StatusUpdates.Add(a.Status); return Task.CompletedTask; }

            public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
                Guid orgId, Guid userId, int page, int pageSize, CancellationToken ct = default) =>
                Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));

            public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
                Guid orgId, Guid userId, string normalizedUrl, string sourceUrl, CancellationToken ct = default) =>
                Task.FromResult<SingleContentAnalysisHistoryItem?>(null);

            public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
                Guid id, Guid orgId, Guid userId, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<string>?>(null);
        }

        public sealed class SimpleUnitOfWork : IUnitOfWork
        {
            public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        }
    }

    private sealed class ConfiguredWorkflowResolver : IWorkflowConfigurationResolver
    {
        private readonly Dictionary<string, IReadOnlyList<WorkflowStepRouteCandidate>> _routes = new();

        public ConfiguredWorkflowResolver With(
            string analysisType,
            string stepKey,
            string? operationKey,
            string? networkKey,
            IReadOnlyList<WorkflowStepRouteCandidate> candidates)
        {
            _routes[Key(analysisType, stepKey, operationKey, networkKey)] = candidates;
            return this;
        }

        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(Result.Success(
                _routes.TryGetValue(
                    Key(request.AnalysisType, request.StepKey, request.OperationKey, request.NetworkKey),
                    out var candidates)
                    ? candidates
                    : []));
        }

        private static string Key(string analysisType, string stepKey, string? operationKey, string? networkKey) =>
            $"{analysisType}|{stepKey}|{operationKey ?? ""}|{networkKey ?? ""}";
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _ = logLevel;
            _ = eventId;
            _ = exception;
            Messages.Add(formatter(state, exception));
        }
    }
}
