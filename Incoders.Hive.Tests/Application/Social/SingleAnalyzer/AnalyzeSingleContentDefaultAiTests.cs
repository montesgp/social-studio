using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Incoders.Hive.Tests.Common;

namespace Incoders.Hive.Tests.Application.Social.SingleAnalyzer;

/// <summary>
/// Tests that SingleContentAnalyzer uses IDefaultAiSelector when no preferred AI provider
/// is supplied in the command (CAP3-MUST-01/02/03/04).
/// </summary>
public sealed class AnalyzeSingleContentDefaultAiTests
{
    private static readonly DateTime Now = new(2026, 5, 23, 10, 0, 0, DateTimeKind.Utc);

    // ─── Default-AI path ─────────────────────────────────────────────────────

    [Fact]
    public async Task Analyzer_UsesDefaultAiSelector_WhenNoPreferredProviderOnCommand()
    {
        var harness = new DefaultAiHarness(Now)
        {
            SelectorResult = Result.Success(new DefaultAiSelection("anthropic", "claude-3-5-sonnet")),
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

        Assert.True(result.IsSuccess);
        Assert.NotNull(harness.AiGateway.LastRequest);
        Assert.Equal("anthropic", harness.AiGateway.LastRequest!.PreferredProviderKey);
        Assert.Equal("claude-3-5-sonnet", harness.AiGateway.LastRequest!.PreferredModel);
        Assert.Equal(1, harness.Selector.CallCount);
    }

    [Fact]
    public async Task Analyzer_FailsWithNoProvider_WhenSelectorReturnsFailure()
    {
        var harness = new DefaultAiHarness(Now)
        {
            SelectorResult = Result.Failure<DefaultAiSelection>(
                IntegrationErrors.DefaultAiProviderNotConnected),
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
        Assert.Equal(IntegrationErrors.DefaultAiProviderNotConnected.Code, result.Error.Code);
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

    private sealed class SpyDefaultAiSelector : IDefaultAiSelector
    {
        private readonly DefaultAiHarness _harness;
        public int CallCount { get; private set; }

        public SpyDefaultAiSelector(DefaultAiHarness harness) => _harness = harness;

        public Task<Result<DefaultAiSelection>> SelectAsync(
            Guid organizationId,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_harness.SelectorResult);
        }
    }

    private sealed class FakeSocialContentResolver : ISocialContentResolver
    {
        private readonly DefaultAiHarness _h;
        public FakeSocialContentResolver(DefaultAiHarness h) => _h = h;

        public Task<Result<SocialContentResolution>> ResolveAsync(
            SocialContentResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_h.ContentResult);
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
        public DefaultAiHarness(DateTime nowUtc, IContentAnalysisProfileProvider? profileProvider = null)
        {
            Repository = new SimpleRepository();
            UnitOfWork = new SimpleUnitOfWork();
            Clock = new TestClock(nowUtc);
            ContentResolver = new FakeSocialContentResolver(this);
            AiGateway = new SpyAiGateway();
            Selector = new SpyDefaultAiSelector(this);

            var provider = profileProvider ?? new StaticProfileProvider(new SpyProfile());

            Analyzer = new SingleContentAnalyzer(
                Repository, UnitOfWork, Clock, ContentResolver, AiGateway, Selector, provider);
        }

        public Result<SocialContentResolution> ContentResult { get; set; } =
            Result.Failure<SocialContentResolution>(IntegrationErrors.ProviderRouteNotConfigured);

        public Result<DefaultAiSelection> SelectorResult { get; set; } =
            Result.Success(new DefaultAiSelection("openai", "gpt-4o"));

        public SimpleRepository Repository { get; }
        public SimpleUnitOfWork UnitOfWork { get; }
        public TestClock Clock { get; }
        public FakeSocialContentResolver ContentResolver { get; }
        public SpyAiGateway AiGateway { get; }
        public SpyDefaultAiSelector Selector { get; }
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
}
