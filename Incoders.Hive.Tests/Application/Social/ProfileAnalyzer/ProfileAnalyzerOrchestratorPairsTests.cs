using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.ContentAnalysis;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Tests.Common;
using DomainProfileAnalysis = Incoders.Hive.Domain.Social.ProfileAnalysis.ProfileAnalysis;

namespace Incoders.Hive.Tests.Application.Social.ProfileAnalyzer;

/// <summary>
/// Tests that <see cref="ProfileAnalyzerOrchestrator"/> builds image↔caption pairs by key-joining
/// archived <see cref="ProfilePostImageRef"/> objects to their parent post on
/// <see cref="ProfilePostImageRef.SourcePostId"/> and passes the result to
/// <see cref="IClientAestheticStyleExtractor.ExtractAsync"/>.
///
/// Verifies CACD-PA-03 (OQ-3): key-join; missing post → null caption; null post.Caption → null;
/// extra posts (no matching ref) dropped; pairs forwarded (not old refs+captions).
/// </summary>
public sealed class ProfileAnalyzerOrchestratorPairsTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);
    private const string OrgInstagramId = "12345";

    // ── CACD-PA-03-a: key-join on SourcePostId ───────────────────────────────

    [Fact]
    public async Task PairBuilding_MatchingPostId_CaptionIsForwarded()
    {
        // Arrange: post P1 has a caption; archiver archives it and returns ref with SourcePostId="P1"
        var orgId = Guid.NewGuid();
        var post = MakePost("P1", imageUrls: ["http://cdn/img1.jpg"], caption: "hello caption");

        // Archiver returns a ref with the same SourcePostId as the post being archived
        var extractor = new CapturePairsExtractor();
        var harness = BuildHarness(orgId, posts: [post],
            archiverResolver: sourcePostId => [MakeRef(sourcePostId)],
            extractor: extractor);

        await harness.Orchestrator.RunQueuedAsync(harness.Analysis.Id, orgId, CancellationToken.None);

        Assert.NotNull(extractor.CapturedPairs);
        var pair = Assert.Single(extractor.CapturedPairs!, p => p.Image.SourcePostId == "P1");
        Assert.Equal("hello caption", pair.Caption);
    }

    // ── CACD-PA-03-b: missing post → null caption ────────────────────────────

    [Fact]
    public async Task PairBuilding_NoMatchingPost_CaptionIsNull_DoesNotThrow()
    {
        // Arrange: archiver returns a ref with SourcePostId="PHANTOM" — no post has PostId="PHANTOM"
        // in postsWithImages. This simulates a ref whose parent post was not in the scraped dataset.
        var orgId = Guid.NewGuid();
        var post = MakePost("P1", imageUrls: ["http://cdn/img1.jpg"], caption: "irrelevant caption");

        // Archiver ignores its sourcePostId argument and returns a ref with a different SourcePostId
        var extractor = new CapturePairsExtractor();
        var harness = BuildHarness(orgId, posts: [post],
            archiverResolver: _ => [MakeRef("PHANTOM")],
            extractor: extractor);

        await harness.Orchestrator.RunQueuedAsync(harness.Analysis.Id, orgId, CancellationToken.None);

        Assert.NotNull(extractor.CapturedPairs);
        // Ref SourcePostId="PHANTOM" has no matching post → Caption = null; no exception
        var pair = Assert.Single(extractor.CapturedPairs!, p => p.Image.SourcePostId == "PHANTOM");
        Assert.Null(pair.Caption);
    }

    // ── CACD-PA-03-d: post exists but Caption is null ────────────────────────

    [Fact]
    public async Task PairBuilding_PostWithNullCaption_PairCaptionIsNull_DoesNotThrow()
    {
        // Arrange: post P1 exists and has images, but its Caption is null
        var orgId = Guid.NewGuid();
        var post = MakePost("P1", imageUrls: ["http://cdn/img1.jpg"], caption: null);

        var extractor = new CapturePairsExtractor();
        var harness = BuildHarness(orgId, posts: [post],
            archiverResolver: sourcePostId => [MakeRef(sourcePostId)],
            extractor: extractor);

        await harness.Orchestrator.RunQueuedAsync(harness.Analysis.Id, orgId, CancellationToken.None);

        Assert.NotNull(extractor.CapturedPairs);
        var pair = Assert.Single(extractor.CapturedPairs!, p => p.Image.SourcePostId == "P1");
        Assert.Null(pair.Caption);
    }

    // ── CACD-PA-03-a drop clause: extra posts without a matching ref are silently dropped ──

    [Fact]
    public async Task PairBuilding_ExtraPostsWithNoMatchingRef_AreDropped()
    {
        // Arrange: two posts (P1 and P3) both have images; archiver only produces a ref for P1.
        // P3 is in postsWithImages but has no matching ref in visionRefs → not in pairs output.
        var orgId = Guid.NewGuid();
        var postP1 = MakePost("P1", imageUrls: ["http://cdn/img1.jpg"], caption: "hello");
        var postP3 = MakePost("P3", imageUrls: ["http://cdn/img3.jpg"], caption: "should not appear");

        var extractor = new CapturePairsExtractor();
        var harness = BuildHarness(orgId, posts: [postP1, postP3],
            // Archiver: returns a ref for P1, returns empty for P3
            archiverResolver: sourcePostId =>
                sourcePostId == "P1"
                    ? [MakeRef("P1")]
                    : [],
            extractor: extractor);

        await harness.Orchestrator.RunQueuedAsync(harness.Analysis.Id, orgId, CancellationToken.None);

        Assert.NotNull(extractor.CapturedPairs);
        // Only P1 should appear; P3 had images but no archived ref → dropped
        Assert.Single(extractor.CapturedPairs!);
        Assert.DoesNotContain(extractor.CapturedPairs!, p => p.Image.SourcePostId == "P3");
    }

    // ── OQ-3: ExtractAsync receives IReadOnlyList<ProfilePostImageCaptionPair> ──

    [Fact]
    public async Task PairBuilding_ExtractAsyncReceivesPairsListWithCorrectSignature()
    {
        // Verifies that ExtractAsync is called with the new pairs signature (not old refs+captions).
        var orgId = Guid.NewGuid();
        var post = MakePost("P1", imageUrls: ["http://cdn/img1.jpg"], caption: "test caption");

        var extractor = new CapturePairsExtractor();
        var harness = BuildHarness(orgId, posts: [post],
            archiverResolver: sourcePostId => [MakeRef(sourcePostId)],
            extractor: extractor);

        await harness.Orchestrator.RunQueuedAsync(harness.Analysis.Id, orgId, CancellationToken.None);

        Assert.True(extractor.WasCalled, "ExtractAsync must be called when enrichment is wired");
        Assert.NotNull(extractor.CapturedPairs);
        Assert.NotEmpty(extractor.CapturedPairs!);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProfilePost MakePost(string postId, IReadOnlyList<string> imageUrls, string? caption) =>
        new(
            PostId: postId,
            MediaType: "Image",
            LikesCount: 100,
            CommentsCount: 10,
            VideoViewCount: null,
            Timestamp: new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            OwnerId: OrgInstagramId,
            OwnerUsername: "testbrand",
            IsPaidPartnership: false,
            IsDegraded: false,
            Hashtags: [],
            CarouselChildrenCount: null,
            ImageUrls: imageUrls,
            Caption: caption);

    private static ProfilePostImageRef MakeRef(string sourcePostId) =>
        new(sourcePostId, 0, $"org/c/a/{sourcePostId}-0.jpg", "hash");

    private sealed record PairsHarnessResult(ProfileAnalyzerOrchestrator Orchestrator, DomainProfileAnalysis Analysis);

    private static PairsHarnessResult BuildHarness(
        Guid orgId,
        IReadOnlyList<ProfilePost> posts,
        Func<string, IReadOnlyList<ProfilePostImageRef>> archiverResolver,
        CapturePairsExtractor extractor)
    {
        var clientId = Guid.NewGuid();
        var ownAccountId = Guid.NewGuid();

        var repository = new FakeRepoPairs();
        var analysis = DomainProfileAnalysis.CreateFromClient(orgId, clientId, ownAccountId, "@testbrand", Now);
        repository.Store(analysis);

        var orchestrator = new ProfileAnalyzerOrchestrator(
            repository,
            new FakeUowPairs(),
            new TestClock(Now),
            new FakeScrapeResolverPairs(orgId, posts),
            new ProfileMetricCalculator(),
            new FakeAiGatewayPairs(MakeAiResult()),
            logger: null,
            providerCallContext: null,
            workflowResolver: new FakeWorkflowResolverPairs(),
            postMediaArchiver: new DelegatingArchiver(archiverResolver),
            aestheticExtractor: extractor);

        return new PairsHarnessResult(orchestrator, analysis);
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static ProfileAnalysisResult MakeAiResult() =>
        new(
            Summary: "Brand summary",
            AudienceProfile: "Young professionals",
            ContentPillars: ["Education"],
            TopContentTypes: ["Reels"],
            PostingDays: ["Mon"],
            DetectedBrandColors: [],
            DetectedFonts: [],
            DetectedBackgroundImages: [],
            VoiceTone: "Direct",
            RecommendedKpis: ["Engagement Rate"],
            MonthlyObjectiveSuggestion: "Grow followers",
            ContentStrategySuggestion: "Post more reels",
            Strengths: ["Consistent"],
            Weaknesses: ["Low views"],
            Opportunities: ["Hashtags"],
            Confidence: 0.8,
            Warnings: []);

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class FakeRepoPairs : IProfileAnalysisRepository
    {
        private DomainProfileAnalysis? _stored;

        public void Store(DomainProfileAnalysis analysis) => _stored = analysis;

        public Task AddAsync(DomainProfileAnalysis analysis, CancellationToken ct = default)
        { _stored = analysis; return Task.CompletedTask; }

        public Task<DomainProfileAnalysis?> GetByIdAsync(Guid analysisId, Guid organizationId, CancellationToken ct = default) =>
            Task.FromResult(_stored?.Id == analysisId && _stored.OrganizationId == organizationId ? _stored : null);

        public Task<ProfileAnalysisHistoryPage> GetByOrganizationAsync(Guid organizationId, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new ProfileAnalysisHistoryPage([], 0, page, pageSize));

        public Task UpdateAsync(DomainProfileAnalysis analysis, CancellationToken ct = default)
        { _stored = analysis; return Task.CompletedTask; }

        public Task<bool> DeleteAsync(Guid analysisId, Guid organizationId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<DateTime?> GetLatestAnalyzedCompletedAtAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
            Task.FromResult<DateTime?>(null);

        public Task<bool> HasNonTerminalAnalysisForClientAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task SetAestheticProfileJsonAsync(Guid analysisId, Guid organizationId, string json, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeUowPairs : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class FakeScrapeResolverPairs(Guid orgId, IReadOnlyList<ProfilePost> posts) : IProfileScrapeResolver
    {
        public Task<Result<ProfileScrapeResult>> ResolveAsync(ProfileScrapeRequest request, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new ProfileScrapeResult(
                Profile: new ProfileSnapshot(
                    OrgInstagramId, "testbrand", 10_000, 50, 5, 3.5, 3.0, null, null, false, true, null),
                OwnerPosts: posts,
                Reels: [],
                ProviderAggregates: new ProfileAggregates(50, 10_000, 200, posts.Count, posts.Count, 0),
                IsPartial: false,
                Diagnostics: [])));
    }

    /// <summary>
    /// Returns valid routes for both the <c>ai-analysis</c> step AND the <c>extract-aesthetic</c> step
    /// so that <c>TryEnrichClientAestheticAsync</c> actually runs.
    /// </summary>
    private sealed class FakeWorkflowResolverPairs : IWorkflowConfigurationResolver
    {
        public Task<Result<IReadOnlyList<WorkflowStepRouteCandidate>>> ResolveStepAsync(
            WorkflowStepResolutionRequest request, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WorkflowStepRouteCandidate> candidates =
            [
                new WorkflowStepRouteCandidate(Guid.NewGuid(), "openai", Guid.NewGuid(),
                    IntegrationConnectionMode.BringYourOwnKey, 1, "gpt-test", null, true),
            ];
            return Task.FromResult(Result.Success(candidates));
        }
    }

    private sealed class FakeAiGatewayPairs(ProfileAnalysisResult payload) : IAiCompletionGateway
    {
        public Task<Result<AiJsonCompletionResponse<TResponse>>> CompleteJsonAsync<TResponse>(
            AiJsonCompletionRequest request,
            CancellationToken cancellationToken = default)
            where TResponse : notnull
        {
            var result = (TResponse)(object)payload;
            return Task.FromResult(Result.Success(
                new AiJsonCompletionResponse<TResponse>(result, "openai", "gpt-test", [])));
        }
    }

    /// <summary>
    /// Archiver that delegates to a caller-provided <see cref="Func{T, TResult}"/>, allowing each
    /// test to configure which refs are returned for which source post ID. This lets us simulate:
    /// <list type="bullet">
    /// <item>Normal archiving: return a ref with the same SourcePostId as the input.</item>
    /// <item>Mismatch (NoMatchingPost): return a ref with a different SourcePostId.</item>
    /// <item>Failed archiving: return an empty list.</item>
    /// </list>
    /// </summary>
    private sealed class DelegatingArchiver(Func<string, IReadOnlyList<ProfilePostImageRef>> resolve)
        : IClientPostMediaArchiver
    {
        public Task<IReadOnlyList<ProfilePostImageRef>> ArchiveAsync(
            Guid organizationId, Guid clientId, Guid analysisId,
            string sourcePostId, IReadOnlyList<string> imageUrls, CancellationToken ct) =>
            Task.FromResult(resolve(sourcePostId));
    }

    /// <summary>
    /// Capture extractor stub — records the pairs list received by
    /// <see cref="IClientAestheticStyleExtractor.ExtractAsync"/>.
    /// </summary>
    private sealed class CapturePairsExtractor : IClientAestheticStyleExtractor
    {
        public bool WasCalled { get; private set; }
        public IReadOnlyList<ProfilePostImageCaptionPair>? CapturedPairs { get; private set; }

        public Task<AestheticExtraction> ExtractAsync(
            Guid organizationId,
            IReadOnlyList<ProfilePostImageCaptionPair> referenceImages,
            CancellationToken ct)
        {
            WasCalled = true;
            CapturedPairs = referenceImages;
            return Task.FromResult(AestheticExtraction.Empty);
        }
    }
}
