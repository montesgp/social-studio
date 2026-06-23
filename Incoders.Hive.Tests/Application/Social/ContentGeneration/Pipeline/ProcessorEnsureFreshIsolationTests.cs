using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// TDD tests for ContentGenerationJobProcessor EnsureFreshAsync exception isolation
/// (design §9 row 6 exception isolation, processor layer).
///
/// CRITICAL: EnsureFreshAsync throwing MUST NOT fail the generation job.
/// The processor wraps the call in try/catch; any exception is logged and the pipeline continues.
///
/// Refs: CAG-CG-04; tasks 4.2.
/// </summary>
public sealed class ProcessorEnsureFreshIsolationTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnalysisId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid JobId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // ── EnsureFreshAsync throws → generation continues, job does NOT fail ──────

    [Fact]
    public async Task ProcessAsync_EnsureFreshThrows_GenerationContinues_JobDoesNotFail()
    {
        var throwingFreshService = new ThrowingEnsureFreshService();
        var profileReader = new IsolationStubProfileReader(null);

        var processor = BuildProcessor(
            ensureFreshService: throwingFreshService,
            profileReader: profileReader);

        // Must not throw
        var exception = await Record.ExceptionAsync(
            () => processor.ProcessAsync(JobId, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ProcessAsync_EnsureFreshThrows_JobStatusIsNotFailed()
    {
        var throwingFreshService = new ThrowingEnsureFreshService();
        var profileReader = new IsolationStubProfileReader(null);
        // Use a special tracking job repo inline
        var statusTracker = new StatusTracker();
        var processor = BuildProcessorWithStatusTracker(
            statusTracker: statusTracker,
            ensureFreshService: throwingFreshService,
            profileReader: profileReader);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        // The job should finish with Completed or Partial — NOT "Failed"
        Assert.DoesNotContain("Failed", statusTracker.FinalStatus ?? string.Empty);
    }

    [Fact]
    public async Task ProcessAsync_EnsureFreshThrows_GetProfileAsyncStillCalled()
    {
        var throwingFreshService = new ThrowingEnsureFreshService();
        var profileReader = new IsolationCountingProfileReader(null);

        var processor = BuildProcessor(
            ensureFreshService: throwingFreshService,
            profileReader: profileReader);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        // Even after EnsureFreshAsync throws, GetProfileAsync MUST still be called (pipeline continues)
        Assert.Equal(1, profileReader.CallCount);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static BrandSnapshot BuildBrand()
    {
        var brandManual = new BrandManualSnapshot([], [], [], [], null, null);
        var clientProfile = new ClientProfileSnapshot("TestClient", null, null, BackgroundSourcingPolicy.ClientAesthetic);
        return BrandSnapshot.FromBrandData(brandManual, null, null, client: clientProfile);
    }

    private static object BuildCarouselPayload() =>
        new
        {
            coverTitle = "Test Carousel",
            ctaText = "Learn More",
            slides = new[] { new { title = "Slide 1", body = "Body 1", imagePromptHint = (string?)null } }
        };

    private static (ContentGenerationJob, SingleContentAnalysisRecord) BuildJobAndAnalysis()
    {
        var fakeJob = new ContentGenerationJob(
            Id: JobId,
            OrganizationId: OrgId,
            ClientId: ClientId,
            AnalysisId: AnalysisId,
            RequestedFormats: ["carousel"],
            Status: "Queued",
            StatusReason: null,
            ErrorCode: null,
            BrandSnapshotJson: "{}",
            ManifestJson: null,
            CorrelationId: Guid.NewGuid(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            StartedAt: null,
            CompletedAt: null);

        var analysis = new SingleAnalyzerAnalysis(
            Summary: "Test",
            DetectedHooks: ["hook"],
            LikelyAudience: "SMBs",
            NarrativeStructure: "problem-solution",
            Strengths: ["good"],
            Weaknesses: [],
            DerivedIdeas: [],
            ClientAdaptationRecommendations: [],
            Confidence: 0.9,
            Warnings: []);

        var analysisJson = JsonSerializer.Serialize(analysis, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var analysisRecord = new SingleContentAnalysisRecord(
            Id: AnalysisId,
            OrganizationId: OrgId,
            ClientId: ClientId,
            UserId: Guid.NewGuid(),
            SourceUrl: "https://example.com/video",
            NormalizedUrl: null,
            SourcePlatformKey: null,
            Status: SingleContentAnalysisStatus.Analyzed,
            ContentSnapshotJson: null,
            AnalysisJson: analysisJson,
            ProviderDiagnosticsJson: null,
            FailureCode: null,
            FailureMessageKey: null,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: DateTime.UtcNow);

        return (fakeJob, analysisRecord);
    }

    private ContentGenerationJobProcessor BuildProcessor(
        IClientAestheticEnsureFreshService? ensureFreshService = null,
        IClientAestheticProfileReader? profileReader = null)
    {
        var brand = BuildBrand();
        var payload = BuildCarouselPayload();
        var (fakeJob, analysisRecord) = BuildJobAndAnalysis();

        return new ContentGenerationJobProcessor(
            jobs: new IsolationStubJobRepository(fakeJob),
            analyses: new IsolationStubAnalysisRepository(analysisRecord),
            orchestrator: new IsolationStubOrchestrator("carousel", payload, brand),
            formatResults: new IsolationStubFormatResultRepository(),
            brandLoader: new IsolationStaticBrandLoader(brand),
            composer: new IsolationSimpleComposer(),
            renderer: new IsolationSimpleRenderer(),
            packageBuilder: new IsolationStubPackageBuilder(),
            packageStorage: new IsolationStubPackageStorage(),
            packages: new IsolationStubPackageRepository(),
            assetStorage: null,
            assetRepository: null,
            designer: null,
            guardrails: null,
            logger: NullLogger<ContentGenerationJobProcessor>.Instance,
            backgroundStrategyResolver: null,
            aestheticEnsureFreshService: ensureFreshService,
            aestheticProfileReader: profileReader);
    }

    private ContentGenerationJobProcessor BuildProcessorWithStatusTracker(
        StatusTracker statusTracker,
        IClientAestheticEnsureFreshService? ensureFreshService = null,
        IClientAestheticProfileReader? profileReader = null)
    {
        var brand = BuildBrand();
        var payload = BuildCarouselPayload();
        var (fakeJob, analysisRecord) = BuildJobAndAnalysis();

        return new ContentGenerationJobProcessor(
            jobs: new IsolationTrackingJobRepository(fakeJob, statusTracker),
            analyses: new IsolationStubAnalysisRepository(analysisRecord),
            orchestrator: new IsolationStubOrchestrator("carousel", payload, brand),
            formatResults: new IsolationStubFormatResultRepository(),
            brandLoader: new IsolationStaticBrandLoader(brand),
            composer: new IsolationSimpleComposer(),
            renderer: new IsolationSimpleRenderer(),
            packageBuilder: new IsolationStubPackageBuilder(),
            packageStorage: new IsolationStubPackageStorage(),
            packages: new IsolationStubPackageRepository(),
            assetStorage: null,
            assetRepository: null,
            designer: null,
            guardrails: null,
            logger: NullLogger<ContentGenerationJobProcessor>.Instance,
            backgroundStrategyResolver: null,
            aestheticEnsureFreshService: ensureFreshService,
            aestheticProfileReader: profileReader);
    }
}

/// <summary>Tracks final job status — shared state passed by reference.</summary>
public sealed class StatusTracker
{
    public string? FinalStatus { get; set; }
}

// ── Fakes / doubles ──────────────────────────────────────────────────────────

/// <summary>EnsureFreshAsync always throws — simulates a catastrophic failure in the refresh path.</summary>
file sealed class ThrowingEnsureFreshService : IClientAestheticEnsureFreshService
{
    public Task<EnsureFreshOutcome> EnsureFreshAsync(Guid orgId, Guid clientId, CancellationToken ct) =>
        throw new InvalidOperationException("Simulated EnsureFreshAsync failure for processor isolation test.");
}

file sealed class IsolationStubProfileReader(ClientAestheticProfile? profile) : IClientAestheticProfileReader
{
    public Task<ClientAestheticProfile?> GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct) =>
        Task.FromResult(profile);
}

file sealed class IsolationCountingProfileReader(ClientAestheticProfile? profile) : IClientAestheticProfileReader
{
    public int CallCount { get; private set; }
    public Task<ClientAestheticProfile?> GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(profile);
    }
}

file sealed class IsolationStubJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class IsolationTrackingJobRepository(ContentGenerationJob job, StatusTracker tracker) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct)
    {
        tracker.FinalStatus = status;
        return Task.CompletedTask;
    }
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class IsolationStubAnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
{
    public Task AddAsync(SingleContentAnalysisRecord a, CancellationToken ct = default) => Task.CompletedTask;
    public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct = default) => Task.FromResult<SingleContentAnalysisRecord?>(record);
    public Task UpdateStatusAsync(Guid id, Guid orgId, SingleContentAnalysisStatus status, DateTime updatedAt, CancellationToken ct = default, string? failCode = null, string? failMsgKey = null) => Task.CompletedTask;
    public Task UpdateAsync(SingleContentAnalysisRecord a, CancellationToken ct = default) => Task.CompletedTask;
    public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(Guid orgId, Guid userId, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));
    public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(Guid orgId, Guid userId, string normalized, string src, CancellationToken ct = default) => Task.FromResult<SingleContentAnalysisHistoryItem?>(null);
    public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(Guid id, Guid orgId, Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>?>([]);
}

file sealed class IsolationStubFormatResultRepository : IContentGenerationFormatResultRepository
{
    private readonly Dictionary<(Guid, string), ContentGenerationFormatResult> _store = new();
    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult r, CancellationToken ct)
    {
        _store[(r.JobId, r.FormatKey)] = r;
        return Task.FromResult(r);
    }
    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>([]);
}

file sealed class IsolationStubOrchestrator(string formatKey, object payload, BrandSnapshot brand) : IContentGenerationOrchestrator
{
    public Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken ct)
    {
        var results = new Dictionary<string, FormatGenerationResult<object>>(StringComparer.OrdinalIgnoreCase)
        {
            [formatKey] = FormatGenerationResult<object>.Succeeded(payload, "fake.v1", [])
        };
        return Task.FromResult(new OrchestratorResult(OrchestratorStatus.Succeeded, results, brand));
    }
}

file sealed class IsolationStaticBrandLoader(BrandSnapshot brand) : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(brand);
}

file sealed class IsolationSimpleComposer : IVisualCompositionService
{
    private static readonly CompositionPalette Palette = new("#111", "#222", "#333", "#FFF", "#EEE");
    private static readonly CompositionTypography Typo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions Dim = new(1080, 1080, 96);

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct) =>
        Task.FromResult(BuildSpec());

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, CarouselBackgroundPlan? plan = null) =>
        Task.FromResult<IReadOnlyList<VisualCompositionSpec>>([BuildSpec()]);

    public IReadOnlyList<string> GetInferredFields() => [];

    private static VisualCompositionSpec BuildSpec() =>
        new(FormatKey: "carousel",
            LayoutType: "grid",
            Palette: Palette,
            Typography: Typo,
            AssetSlots: new Dictionary<string, AssetSlotSpec> { ["slide-1"] = new AssetSlotSpec(null, "bg") },
            CopySlots: new Dictionary<string, string> { ["title"] = "T", ["subtitle"] = "S", ["cta"] = "C" },
            Dimensions: Dim);
}

file sealed class IsolationSimpleRenderer : IContentRenderer
{
    private static readonly byte[] FakePngBytes = [0x89, 0x50, 0x4E, 0x47];
    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct) =>
        Task.FromResult(Result.Success(RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", FakePngBytes)));
}

file sealed class IsolationStubPackageBuilder : IGeneratedPackageBuilder
{
    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-zip"));
        var manifest = new GeneratedPackageManifest(new Dictionary<string, FormatAssetMetadata>(), [], [], 0, input.ClientName ?? "test");
        return Task.FromResult(new BuiltPackage(stream, stream.Length, manifest, "test.zip"));
    }
}

file sealed class IsolationStubPackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class IsolationStubPackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) => Task.FromResult<GeneratedPackage?>(null);
}
