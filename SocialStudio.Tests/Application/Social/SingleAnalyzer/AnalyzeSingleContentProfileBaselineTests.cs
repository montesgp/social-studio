using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.ContentAnalysis;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.SingleAnalyzer.Analyze;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.SingleAnalyzer;
using SocialStudio.Tests.Common;

namespace SocialStudio.Tests.Application.Social.SingleAnalyzer;

/// <summary>
/// Baseline test written in TASK 03 to pin the expected contract once TASK 04 wires
/// <see cref="IContentAnalysisProfileProvider"/> into <see cref="SingleContentAnalyzer"/>.
/// Skipped until TASK 04 is implemented.
/// </summary>
public sealed class AnalyzeSingleContentProfileBaselineTests
{
    private static readonly DateTime Now = new(2026, 5, 18, 13, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Verify that after TASK 04, the handler reads FlowKey and capabilities from the injected profile.
    /// Un-skipped in TASK 04 — SingleContentAnalyzer now accepts IContentAnalysisProfileProvider.
    /// </summary>
    [Fact]
    public async Task Handler_ReadsFlowKeyAndCapabilitiesFromProfile()
    {
        var spyProfile = new SpyProfile();
        var provider = new StaticProfileProvider(spyProfile);

        var harness = new SingleAnalyzerHarnessWithProfile(Now, provider)
        {
            ContentResult = Result.Success(new SocialContentResolution(
                Snapshot("tiktok", []),
                []))
        };

        await harness.Analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://www.tiktok.com/@brand/video/123",
                ClientId: null,
                PreferredExtractionProviderKey: null),
            CancellationToken.None);

        Assert.True(spyProfile.DetectPlatformCalled, "Profile.DetectPlatform should have been called");
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

    /// <summary>
    /// Returns one AI route so the analyzer can complete its workflow-driven AI selection step.
    /// </summary>
    private sealed class StaticWorkflowResolver : IWorkflowConfigurationResolver
    {
        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<WorkflowStepRouteCandidate>>(
            [
                new WorkflowStepRouteCandidate(Guid.NewGuid(), "openai", Guid.NewGuid(),
                    IntegrationConnectionMode.BringYourOwnKey, 1, "gpt-test", null, true)
            ]));
    }

    /// <summary>
    /// Extended harness placeholder for TASK 04. Currently identical to SingleAnalyzerHarness
    /// from the main handler tests; TASK 04 will pass <paramref name="provider"/> to the analyzer.
    /// </summary>
    private sealed class SingleAnalyzerHarnessWithProfile
    {
        private readonly IContentAnalysisProfileProvider _provider;

        public SingleAnalyzerHarnessWithProfile(DateTime nowUtc, IContentAnalysisProfileProvider provider)
        {
            _provider = provider;
            Repository = new InMemorySingleContentAnalysisRepository();
            UnitOfWork = new TestUnitOfWork();
            Clock = new TestClock(nowUtc);
            ContentResolver = new FakeSocialContentResolver(this);
            AiGateway = new FakeAiGateway();

            // SingleContentAnalyzer resolves AI solely from the workflow config (no Default-AI selector).
            Analyzer = new SingleContentAnalyzer(
                Repository, UnitOfWork, Clock, ContentResolver, AiGateway,
                _provider, workflowResolver: new StaticWorkflowResolver());
        }

        public Result<SocialContentResolution> ContentResult { get; set; } =
            Result.Failure<SocialContentResolution>(IntegrationErrors.ProviderRouteNotConfigured);

        public InMemorySingleContentAnalysisRepository Repository { get; }
        public TestUnitOfWork UnitOfWork { get; }
        public TestClock Clock { get; }
        public FakeSocialContentResolver ContentResolver { get; }
        public FakeAiGateway AiGateway { get; }
        public SingleContentAnalyzer Analyzer { get; }

        // ─── Fakes ───────────────────────────────────────────────────────────

        public sealed class FakeSocialContentResolver : ISocialContentResolver
        {
            private readonly SingleAnalyzerHarnessWithProfile _h;
            public FakeSocialContentResolver(SingleAnalyzerHarnessWithProfile h) => _h = h;

            public Task<Result<SocialContentResolution>> ResolveAsync(
                SocialContentResolutionRequest request,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(_h.ContentResult);
        }

        public sealed class FakeAiGateway : IAiCompletionGateway
        {
            public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
                AiJsonCompletionRequest request,
                CancellationToken cancellationToken = default)
                where TResponse : notnull
            {
                var analysis = (TResponse)(object)CreateAnalysis();
                return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(
                    analysis, "openai", "gpt-test", [])));
            }

            private static SingleAnalyzerAnalysis CreateAnalysis() =>
                SingleAnalyzerAnalysis.Create(
                    "summary", ["hook"], "audience", "structure", ["strength"], ["weakness"],
                    [SingleAnalyzerIdea.Create("title", "concept", "rationale",
                        SingleAnalyzerScore.Create(3, 3, 3, 3, 3, 3).Value).Value],
                    ["recommendation"], 0.8, []).Value;
        }

        public sealed class InMemorySingleContentAnalysisRepository : ISingleContentAnalysisRepository
        {
            public SingleContentAnalysisRecord? StoredAnalysis { get; private set; }
            public List<SingleContentAnalysisStatus> StatusUpdates { get; } = [];

            public Task AddAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default)
            { StoredAnalysis = analysis; return Task.CompletedTask; }

            public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid analysisId, Guid organizationId, CancellationToken cancellationToken = default) =>
                Task.FromResult(StoredAnalysis?.Id == analysisId ? StoredAnalysis : null);

            public Task UpdateStatusAsync(Guid analysisId, Guid organizationId, SingleContentAnalysisStatus status,
                DateTime updatedAtUtc, CancellationToken cancellationToken = default,
                string? failureCode = null, string? failureMessageKey = null)
            {
                StatusUpdates.Add(status);
                return Task.CompletedTask;
            }

            public Task UpdateAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default)
            { StoredAnalysis = analysis; StatusUpdates.Add(analysis.Status); return Task.CompletedTask; }

            public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
                Guid organizationId, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default) =>
                Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));

            public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
                Guid organizationId, Guid userId, string normalizedUrl, string sourceUrl, CancellationToken cancellationToken = default) =>
                Task.FromResult<SingleContentAnalysisHistoryItem?>(null);

            public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
                Guid analysisId, Guid organizationId, Guid userId, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<string>?>(null);
        }

        public sealed class TestUnitOfWork : IUnitOfWork
        {
            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        }
    }
}
