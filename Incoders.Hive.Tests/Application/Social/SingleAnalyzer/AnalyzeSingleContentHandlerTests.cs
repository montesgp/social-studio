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

public sealed class AnalyzeSingleContentHandlerTests
{
    private static readonly DateTime Now = new(2026, 5, 18, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handler_ResolvesContentAnalyzesWithAiAndPersistsStructuredResult()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var harness = new SingleAnalyzerHarness(Now)
        {
            ContentResult = Result.Success(new SocialContentResolution(
                Snapshot("tiktok", warnings: []),
                [Diagnostic("social-extraction", "apify")])),
            AiPayload = Analysis("The creator opens with a sharp problem statement.")
        };

        var result = await harness.Analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                organizationId,
                userId,
                " HTTPS://WWW.TIKTOK.COM/@brand/video/123#comments ",
                ClientId: null,
                PreferredExtractionProviderKey: "apify"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(organizationId, result.Value.OrganizationId);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Equal("https://www.tiktok.com/@brand/video/123", result.Value.NormalizedUrl);
        Assert.Equal("tiktok", result.Value.PlatformKey);
        Assert.Equal(SingleContentAnalysisStatus.Analyzed, result.Value.Status);
        Assert.Equal("The creator opens with a sharp problem statement.", result.Value.Analysis.Summary);
        Assert.Equal("openai", result.Value.AiProviderKey);
        Assert.Equal("gpt-test", result.Value.AiModel);

        Assert.NotNull(harness.ContentResolver.LastRequest);
        Assert.Equal(result.Value.Id, harness.ContentResolver.LastRequest!.AnalysisId);
        Assert.Equal("tiktok", harness.ContentResolver.LastRequest!.PlatformHint);
        Assert.Equal("apify", harness.ContentResolver.LastRequest.PreferredProviderKey);

        Assert.NotNull(harness.AiGateway.LastRequest);
        Assert.Equal(result.Value.Id, harness.AiGateway.LastRequest!.AnalysisId);
        Assert.Equal("ai-analysis", harness.AiGateway.LastRequest!.CapabilityKey);
        Assert.Equal("single-analyzer.analysis.v1", harness.AiGateway.LastRequest.PromptVersionKey);
        Assert.Equal("openai", harness.AiGateway.LastRequest.PreferredProviderKey);
        Assert.Equal("gpt-test", harness.AiGateway.LastRequest.PreferredModel);

        Assert.Equal(
            [
                SingleContentAnalysisStatus.ResolvingContent,
                SingleContentAnalysisStatus.ContentResolved,
                SingleContentAnalysisStatus.Analyzing,
                SingleContentAnalysisStatus.Analyzed
            ],
            harness.Repository.StatusUpdates);
        Assert.Contains("\"summary\"", harness.Repository.StoredAnalysis!.AnalysisJson);
        Assert.Contains("\"platformKey\":\"tiktok\"", harness.Repository.StoredAnalysis.ContentSnapshotJson);
        Assert.Contains("\"providerKey\":\"apify\"", harness.Repository.StoredAnalysis.ProviderDiagnosticsJson);
    }

    [Fact]
    public async Task Handler_PassesUnknownPlatformForFutureConnectorUrls()
    {
        var harness = new SingleAnalyzerHarness(Now)
        {
            ContentResult = Result.Success(new SocialContentResolution(
                Snapshot("unknown", canonicalUrl: "https://example.com/posts/abc", warnings: []),
                [])),
            AiPayload = Analysis("A generic social post can still be analyzed.")
        };

        var result = await harness.Analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://example.com/posts/abc",
                ClientId: null,
                PreferredExtractionProviderKey: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("unknown", harness.ContentResolver.LastRequest!.PlatformHint);
        Assert.Equal("unknown", result.Value.PlatformKey);
    }

    [Fact]
    public async Task Handler_MarksContentResolvedPartialWhenSnapshotHasWarnings()
    {
        var warning = new SocialContentWarning("transcript_missing", "social.single_analyzer.warnings.transcript_missing");
        var harness = new SingleAnalyzerHarness(Now)
        {
            ContentResult = Result.Success(new SocialContentResolution(
                Snapshot("instagram", warnings: [warning]),
                [])),
            AiPayload = Analysis("A reel with useful visual hooks.", warnings: ["Transcript was unavailable."])
        };

        var result = await harness.Analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://www.instagram.com/reel/abc",
                ClientId: null,
                PreferredExtractionProviderKey: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(SingleContentAnalysisStatus.ContentResolvedPartial, harness.Repository.StatusUpdates);
        Assert.Contains(result.Value.Warnings, item => item.Code == "transcript_missing");
        Assert.Contains("Transcript was unavailable.", result.Value.Analysis.Warnings);
    }

    [Fact]
    public async Task Handler_MarksFailedWhenAiAnalysisFails()
    {
        var harness = new SingleAnalyzerHarness(Now)
        {
            ContentResult = Result.Success(new SocialContentResolution(Snapshot("youtube", warnings: []), [])),
            AiResult = Result.Failure<AiJsonCompletionResponse<SingleAnalyzerAnalysis>>(IntegrationErrors.AiJsonInvalid)
        };

        var result = await harness.Analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://youtu.be/abc123",
                ClientId: null,
                PreferredExtractionProviderKey: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("integrations.ai_json_invalid", result.Error.Code);
        Assert.Equal(SingleContentAnalysisStatus.Failed, harness.Repository.StoredAnalysis!.Status);
        Assert.Equal("integrations.ai_json_invalid", harness.Repository.StoredAnalysis.FailureCode);
        Assert.Equal(1, harness.Repository.StoredAnalysis.AiAnalysisFailedAttemptCount);
        Assert.Null(harness.Repository.StoredAnalysis.AiAnalysisRetryBlockedAtUtc);
        Assert.Null(harness.Repository.StoredAnalysis.AnalysisJson);
    }

    [Fact]
    public async Task Handler_BlocksRetryAiAfterSecondFailedAiAttempt()
    {
        var organizationId = Guid.NewGuid();
        var harness = new SingleAnalyzerHarness(Now)
        {
            ContentResult = Result.Success(new SocialContentResolution(Snapshot("youtube", warnings: []), [])),
            AiResult = Result.Failure<AiJsonCompletionResponse<SingleAnalyzerAnalysis>>(IntegrationErrors.AiJsonInvalid)
        };

        var first = await harness.Analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                organizationId,
                Guid.NewGuid(),
                "https://youtu.be/abc123",
                ClientId: null,
                PreferredExtractionProviderKey: null),
            CancellationToken.None);

        Assert.True(first.IsFailure);

        await harness.Analyzer.RetryAiOnlyAsync(
            harness.Repository.StoredAnalysis!.Id,
            organizationId,
            CancellationToken.None);

        Assert.Equal(2, harness.Repository.StoredAnalysis!.AiAnalysisFailedAttemptCount);
        Assert.NotNull(harness.Repository.StoredAnalysis.AiAnalysisRetryBlockedAtUtc);

        var retryHandler = new RetryAiAnalysisHandler(
            harness.Repository,
            harness.UnitOfWork,
            harness.Clock,
            harness.Queue);

        var retry = await retryHandler.Handle(
            new RetryAiAnalysisCommand(harness.Repository.StoredAnalysis.Id, organizationId),
            CancellationToken.None);

        Assert.True(retry.IsFailure);
        Assert.Equal("social.single_analyzer.ai_retry_blocked", retry.Error.Code);
        Assert.DoesNotContain(harness.Queue.Enqueued, job => job.Kind == SingleAnalysisJobKind.RetryAi);
    }

    [Fact]
    public async Task Handler_ReturnsValidationFailureAndDoesNotCallProvidersWhenUrlInvalid()
    {
        var harness = new SingleAnalyzerHarness(Now);

        var result = await harness.Analyzer.AnalyzeAsync(
            new SingleContentAnalysisRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "not a url",
                ClientId: null,
                PreferredExtractionProviderKey: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.single_analyzer.url_invalid", result.Error.Code);
        Assert.Null(harness.ContentResolver.LastRequest);
        Assert.Null(harness.AiGateway.LastRequest);
        Assert.Null(harness.Repository.StoredAnalysis);
    }

    [Fact]
    public async Task Handler_StartsAnalysisAndEnqueuesBackgroundJobWithoutRunningPipeline()
    {
        var organizationId = Guid.NewGuid();
        var harness = new SingleAnalyzerHarness(Now)
        {
            ContentResult = Result.Success(new SocialContentResolution(Snapshot("tiktok", warnings: []), [])),
            AiPayload = Analysis("queued")
        };

        var result = await harness.Handler.Handle(
            new AnalyzeSingleContentCommand(
                organizationId,
                Guid.NewGuid(),
                "https://www.tiktok.com/@brand/video/123",
                ClientId: null,
                PreferredExtractionProviderKey: "apify"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SingleContentAnalysisStatus.Requested, result.Value.Status);
        Assert.NotEqual(Guid.Empty, result.Value.Id);

        var job = Assert.Single(harness.Queue.Enqueued);
        Assert.Equal(result.Value.Id, job.AnalysisId);
        Assert.Equal(organizationId, job.OrganizationId);

        // The heavy extraction + AI work must run in the background, NOT on the request thread.
        Assert.Null(harness.ContentResolver.LastRequest);
        Assert.Null(harness.AiGateway.LastRequest);
    }

    private static SocialContentSnapshot Snapshot(
        string platform,
        IReadOnlyList<SocialContentWarning> warnings,
        string? canonicalUrl = null) =>
        new(
            platform,
            canonicalUrl ?? $"https://www.{platform}.com/@brand/video/123",
            Title: "Hook example",
            Description: "Caption with context",
            Author: "@brand",
            ThumbnailUrl: "https://cdn.example.com/thumb.jpg",
            DurationSeconds: 38,
            Metrics: new Dictionary<string, long>
            {
                ["views"] = 120_000,
                ["likes"] = 8_500
            },
            Transcript: "Stop scrolling if your content is not converting.",
            MediaReferences:
            [
                new SocialMediaReference("video", "https://cdn.example.com/video.mp4", "video/mp4")
            ],
            RawPayloadReference: "provider://apify/runs/1",
            Warnings: warnings,
            Tags: ["conversion"],
            ContentType: "video",
            PublishedAtUtc: null);

    private static ProviderFailureDiagnostic Diagnostic(string capability, string provider) =>
        ProviderFailureDiagnostic.Create(
            capability,
            provider,
            attemptNumber: 1,
            failureCode: "provider_timeout",
            messageKey: "integrations.errors.provider_timeout",
            isRetryable: true,
            fallbackAllowed: true,
            elapsedMs: 250,
            occurredAtUtc: Now).Value;

    private static SingleAnalyzerAnalysis Analysis(string summary, IReadOnlyList<string>? warnings = null) =>
        SingleAnalyzerAnalysis.Create(
            summary,
            detectedHooks: ["Stop scrolling if your content is not converting."],
            likelyAudience: "Founders and content strategists",
            narrativeStructure: "Hook > problem > proof > recommendation",
            strengths: ["Clear first sentence", "Concrete pain point"],
            weaknesses: ["CTA could be stronger"],
            derivedIdeas:
            [
                SingleAnalyzerIdea.Create(
                    "Before/after proof reel",
                    "Show the client's workflow before and after the service.",
                    "Maps the original hook to a service proof angle.",
                    SingleAnalyzerScore.Create(4, 5, 4, 5, 4, 4).Value).Value
            ],
            clientAdaptationRecommendations: ["Replace generic proof with client-specific metrics."],
            confidence: 0.86,
            warnings: warnings ?? []).Value;

    private sealed class SingleAnalyzerHarness
    {
        public SingleAnalyzerHarness(DateTime nowUtc)
        {
            Repository = new InMemorySingleContentAnalysisRepository();
            UnitOfWork = new TestUnitOfWork();
            Clock = new TestClock(nowUtc);
            ContentResolver = new FakeSocialContentResolver(this);
            AiGateway = new FakeAiGateway(this);

            Queue = new FakeQueue();
            Analyzer = new SingleContentAnalyzer(
                Repository,
                UnitOfWork,
                Clock,
                ContentResolver,
                AiGateway,
                new NullDefaultAiSelector(),
                new NullProfileProvider());
            Handler = new AnalyzeSingleContentHandler(Analyzer, Queue);
        }

        public Result<SocialContentResolution> ContentResult { get; set; } =
            Result.Failure<SocialContentResolution>(IntegrationErrors.ProviderRouteNotConfigured);

        public SingleAnalyzerAnalysis AiPayload { get; set; } = Analysis("Default analysis");

        public Result<AiJsonCompletionResponse<SingleAnalyzerAnalysis>>? AiResult { get; set; }

        public InMemorySingleContentAnalysisRepository Repository { get; }

        public TestUnitOfWork UnitOfWork { get; }

        public TestClock Clock { get; }

        public FakeSocialContentResolver ContentResolver { get; }

        public FakeAiGateway AiGateway { get; }

        public FakeQueue Queue { get; }

        public SingleContentAnalyzer Analyzer { get; }

        public AnalyzeSingleContentHandler Handler { get; }
    }

    private sealed class FakeQueue : ISingleAnalysisJobQueue
    {
        public List<SingleAnalysisJob> Enqueued { get; } = [];

        public ValueTask EnqueueAsync(SingleAnalysisJob job, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(job);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<SingleAnalysisJob> DequeueAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSocialContentResolver : ISocialContentResolver
    {
        private readonly SingleAnalyzerHarness _harness;

        public FakeSocialContentResolver(SingleAnalyzerHarness harness)
        {
            _harness = harness;
        }

        public SocialContentResolutionRequest? LastRequest { get; private set; }

        public Task<Result<SocialContentResolution>> ResolveAsync(
            SocialContentResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_harness.ContentResult);
        }
    }

    private sealed class FakeAiGateway : IAiCompletionGateway
    {
        private readonly SingleAnalyzerHarness _harness;

        public FakeAiGateway(SingleAnalyzerHarness harness)
        {
            _harness = harness;
        }

        public AiJsonCompletionRequest? LastRequest { get; private set; }

        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            LastRequest = request;

            if (_harness.AiResult is { IsFailure: true } failure)
            {
                return Task.FromResult(Result.Failure<AiJsonCompletionResponse<TResponse>>(failure.Error));
            }

            var payload = (TResponse)(object)_harness.AiPayload;
            return Task.FromResult(Result.Success(new AiJsonCompletionResponse<TResponse>(
                payload,
                "openai",
                "gpt-test",
                [Diagnostic("ai-analysis", "openai")])));
        }
    }

    private sealed class InMemorySingleContentAnalysisRepository : ISingleContentAnalysisRepository
    {
        public SingleContentAnalysisRecord? StoredAnalysis { get; private set; }

        public List<SingleContentAnalysisStatus> StatusUpdates { get; } = [];

        public Task AddAsync(
            SingleContentAnalysisRecord analysis,
            CancellationToken cancellationToken = default)
        {
            StoredAnalysis = analysis;
            return Task.CompletedTask;
        }

        public Task<SingleContentAnalysisRecord?> GetByIdAsync(
            Guid analysisId,
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                StoredAnalysis?.Id == analysisId && StoredAnalysis.OrganizationId == organizationId
                    ? StoredAnalysis
                    : null);

        public Task UpdateStatusAsync(
            Guid analysisId,
            Guid organizationId,
            SingleContentAnalysisStatus status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken = default,
            string? failureCode = null,
            string? failureMessageKey = null)
        {
            if (StoredAnalysis is null)
            {
                return Task.CompletedTask;
            }

            StatusUpdates.Add(status);
            StoredAnalysis = StoredAnalysis with
            {
                Status = status,
                UpdatedAtUtc = updatedAtUtc,
                CompletedAtUtc = status is SingleContentAnalysisStatus.Analyzed or SingleContentAnalysisStatus.Failed
                    ? updatedAtUtc
                    : StoredAnalysis.CompletedAtUtc,
                FailureCode = failureCode,
                FailureMessageKey = failureMessageKey
            };

            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            SingleContentAnalysisRecord analysis,
            CancellationToken cancellationToken = default)
        {
            StoredAnalysis = analysis;
            StatusUpdates.Add(analysis.Status);
            return Task.CompletedTask;
        }

        public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
            Guid organizationId,
            Guid userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));

        public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
            Guid organizationId,
            Guid userId,
            string normalizedUrl,
            string sourceUrl,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SingleContentAnalysisHistoryItem?>(null);

        public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
            Guid analysisId,
            Guid organizationId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>?>(null);
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// AI selector stub used in existing handler tests. Returns a default selection so the
    /// analyzer reaches the AI gateway (the behavior being tested). Dedicated default-AI
    /// selection tests live in AnalyzeSingleContentDefaultAiTests.
    /// </summary>
    private sealed class NullDefaultAiSelector : IDefaultAiSelector
    {
        public Task<Result<DefaultAiSelection>> SelectAsync(Guid organizationId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new DefaultAiSelection("openai", "gpt-test")));
    }

    /// <summary>
    /// No-op profile provider — returns failure so the analyzer falls back to its own constants.
    /// Existing handler tests do not exercise the profile path (TASK 04 tests do).
    /// </summary>
    private sealed class NullProfileProvider : IContentAnalysisProfileProvider
    {
        public Result<IContentAnalysisProfile> ForFlow(string flowKey) =>
            Result.Failure<IContentAnalysisProfile>(new Error("test.no_profile", "No profile", ErrorType.NotFound));
    }
}
