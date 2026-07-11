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
/// TDD tests for ContentGenerationJobProcessor aesthetic wiring (design §9 row 7).
/// Verifies that when policy == ClientAesthetic:
///   - EnsureFreshAsync is called exactly once
///   - GetProfileAsync is called exactly once
///   - AestheticProfile is threaded into OrchestratorRequest
///   - BackgroundDescriptors are threaded into BackgroundPlanRequest
/// Also verifies non-CaseC jobs: neither EnsureFreshAsync nor GetProfileAsync is called.
///
/// Refs: CAG-CG-06, CAG-CG-07, CAG-CG-08; tasks 4.1.
/// </summary>
public sealed class ProcessorAestheticWiringTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnalysisId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid JobId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly ClientAestheticProfile SampleProfile = new(
        BackgroundDescriptors: ["dark studio", "minimalist"],
        VoiceTone: "professional",
        AverageSlidesPerCarousel: 5.0,
        AnalyzedAtUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

    // ── ClientAesthetic policy: ensure-fresh + profile read + threading ────────

    [Fact]
    public async Task ProcessAsync_ClientAestheticPolicy_EnsureFreshCalledOnce()
    {
        var freshService = new CountingEnsureFreshService(EnsureFreshOutcome.Fresh);
        var profileReader = new StubAestheticProfileReader(SampleProfile);

        var processor = BuildProcessor(
            policy: BackgroundSourcingPolicy.ClientAesthetic,
            ensureFreshService: freshService,
            profileReader: profileReader);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        Assert.Equal(1, freshService.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_ClientAestheticPolicy_GetProfileAsyncCalledOnce()
    {
        var freshService = new CountingEnsureFreshService(EnsureFreshOutcome.Fresh);
        var profileReader = new CountingAestheticProfileReader(SampleProfile);

        var processor = BuildProcessor(
            policy: BackgroundSourcingPolicy.ClientAesthetic,
            ensureFreshService: freshService,
            profileReader: profileReader);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        Assert.Equal(1, profileReader.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_ClientAestheticPolicy_AestheticProfileThreadedIntoOrchestratorRequest()
    {
        var freshService = new CountingEnsureFreshService(EnsureFreshOutcome.Fresh);
        var profileReader = new StubAestheticProfileReader(SampleProfile);
        var capturingOrchestrator = new CapturingOrchestrator("carousel", BuildCarouselPayload(), BuildBrand(BackgroundSourcingPolicy.ClientAesthetic));

        var processor = BuildProcessorWithOrchestrator(
            policy: BackgroundSourcingPolicy.ClientAesthetic,
            ensureFreshService: freshService,
            profileReader: profileReader,
            orchestrator: capturingOrchestrator);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        Assert.NotNull(capturingOrchestrator.LastRequest);
        Assert.NotNull(capturingOrchestrator.LastRequest.AestheticProfile);
        Assert.Equal("professional", capturingOrchestrator.LastRequest.AestheticProfile!.VoiceTone);
    }

    [Fact]
    public async Task ProcessAsync_ClientAestheticPolicy_DescriptorsThreadedIntoBackgroundPlanRequest()
    {
        var freshService = new CountingEnsureFreshService(EnsureFreshOutcome.Fresh);
        var profileReader = new StubAestheticProfileReader(SampleProfile);
        var capturingStrategy = new CapturingBackgroundStrategy();
        var resolver = new FixedBackgroundStrategyResolver(capturingStrategy);

        var processor = BuildProcessor(
            policy: BackgroundSourcingPolicy.ClientAesthetic,
            ensureFreshService: freshService,
            profileReader: profileReader,
            backgroundStrategyResolver: resolver);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        Assert.NotNull(capturingStrategy.LastRequest);
        Assert.Equal(["dark studio", "minimalist"], capturingStrategy.LastRequest!.ClientAestheticDescriptors);
    }

    // ── Non-CaseC: neither ensure-fresh nor profile-read is called ─────────────

    [Fact]
    public async Task ProcessAsync_NullPolicy_NeitherEnsureFreshNorProfileReaderCalled()
    {
        var freshService = new CountingEnsureFreshService(EnsureFreshOutcome.Fresh);
        var profileReader = new CountingAestheticProfileReader(SampleProfile);

        var processor = BuildProcessor(
            policy: null,          // UNSET → not ClientAesthetic
            ensureFreshService: freshService,
            profileReader: profileReader);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        Assert.Equal(0, freshService.CallCount);
        Assert.Equal(0, profileReader.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_AiPalettePolicy_NeitherEnsureFreshNorProfileReaderCalled()
    {
        var freshService = new CountingEnsureFreshService(EnsureFreshOutcome.Fresh);
        var profileReader = new CountingAestheticProfileReader(SampleProfile);

        var processor = BuildProcessor(
            policy: BackgroundSourcingPolicy.AiPalette,
            ensureFreshService: freshService,
            profileReader: profileReader);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        Assert.Equal(0, freshService.CallCount);
        Assert.Equal(0, profileReader.CallCount);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static BrandSnapshot BuildBrand(BackgroundSourcingPolicy? policy)
    {
        var brandManual = new BrandManualSnapshot(
            ColorPalette: [],
            Logos: [],
            BackgroundTemplates: [],
            Typography: [],
            VoiceAndTone: null,
            Notes: null);

        var clientProfile = new ClientProfileSnapshot("TestClient", null, null, policy);
        return BrandSnapshot.FromBrandData(brandManual, null, null, client: clientProfile);
    }

    private static object BuildCarouselPayload() =>
        new
        {
            coverTitle = "Test Carousel",
            ctaText = "Learn More",
            slides = new[] { new { title = "Slide 1", body = "Body 1", imagePromptHint = (string?)null } }
        };

    private ContentGenerationJobProcessor BuildProcessor(
        BackgroundSourcingPolicy? policy,
        IClientAestheticEnsureFreshService? ensureFreshService = null,
        IClientAestheticProfileReader? profileReader = null,
        IBackgroundStrategyResolver? backgroundStrategyResolver = null)
    {
        var brand = BuildBrand(policy);
        var payload = BuildCarouselPayload();
        var orchestrator = new AwWiringStubOrchestrator("carousel", payload, brand);
        return BuildProcessorWithOrchestrator(policy, ensureFreshService, profileReader, orchestrator, backgroundStrategyResolver);
    }

    private ContentGenerationJobProcessor BuildProcessorWithOrchestrator(
        BackgroundSourcingPolicy? policy,
        IClientAestheticEnsureFreshService? ensureFreshService = null,
        IClientAestheticProfileReader? profileReader = null,
        IContentGenerationOrchestrator? orchestrator = null,
        IBackgroundStrategyResolver? backgroundStrategyResolver = null)
    {
        var brand = BuildBrand(policy);
        var payload = BuildCarouselPayload();

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
            DetectedHooks: ["hook1"],
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

        return new ContentGenerationJobProcessor(
            jobs: new AwWiringStubJobRepository(fakeJob),
            analyses: new AwWiringStubAnalysisRepository(analysisRecord),
            orchestrator: orchestrator ?? new AwWiringStubOrchestrator("carousel", payload, brand),
            formatResults: new AwWiringStubFormatResultRepository(),
            brandLoader: new AwWiringStaticBrandLoader(brand),
            composer: new AwWiringSimpleComposer(),
            renderer: new AwWiringSimpleRenderer(),
            packageBuilder: new AwWiringStubPackageBuilder(),
            packageStorage: new AwWiringStubPackageStorage(),
            packages: new AwWiringStubPackageRepository(),
            assetStorage: null,
            assetRepository: null,
            designer: null,
            guardrails: null,
            logger: NullLogger<ContentGenerationJobProcessor>.Instance,
            backgroundStrategyResolver: backgroundStrategyResolver,
            aestheticEnsureFreshService: ensureFreshService,
            aestheticProfileReader: profileReader);
    }
}

// ── Fakes / doubles ──────────────────────────────────────────────────────────

file sealed class CountingEnsureFreshService(EnsureFreshOutcome outcome) : IClientAestheticEnsureFreshService
{
    public int CallCount { get; private set; }

    public Task<EnsureFreshOutcome> EnsureFreshAsync(Guid orgId, Guid clientId, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(outcome);
    }

    // Usable-present branch: keeps this fake exercising the fire-and-forget EnsureFreshAsync path
    // (design Decision 1) rather than the new blocking branch.
    public Task<bool> LatestUsableAestheticPresentAsync(Guid orgId, Guid clientId, CancellationToken ct) =>
        Task.FromResult(true);

    public Task<EnsureAvailableOutcome> EnsureAvailableBlockingAsync(
        Guid orgId, Guid clientId, TimeSpan maxWait, Func<CancellationToken, Task>? onWaitStarted, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised — this fake reports the aesthetic as already usable.");
}

file sealed class StubAestheticProfileReader(ClientAestheticProfile? profile) : IClientAestheticProfileReader
{
    public Task<ClientAestheticProfile?> GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct) =>
        Task.FromResult(profile);
}

file sealed class CountingAestheticProfileReader(ClientAestheticProfile? profile) : IClientAestheticProfileReader
{
    public int CallCount { get; private set; }

    public Task<ClientAestheticProfile?> GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(profile);
    }
}

file sealed class CapturingBackgroundStrategy : IBackgroundSourcingStrategy
{
    public BackgroundPlanRequest? LastRequest { get; private set; }
    public BackgroundSourcingPolicy Policy => BackgroundSourcingPolicy.ClientAesthetic;

    public CarouselBackgroundPlan Plan(BackgroundPlanRequest request)
    {
        LastRequest = request;
        // Return a simple 1-slide Creative plan
        return new CarouselBackgroundPlan(
            Policy,
            [(BackgroundSelection)new BackgroundSelection.Creative()],
            request.SeedToken);
    }
}

file sealed class FixedBackgroundStrategyResolver(IBackgroundSourcingStrategy strategy) : IBackgroundStrategyResolver
{
    public IBackgroundSourcingStrategy Resolve(BackgroundSourcingPolicy? policy) => strategy;
}

file sealed class CapturingOrchestrator(string formatKey, object payload, BrandSnapshot brand) : IContentGenerationOrchestrator
{
    public OrchestratorRequest? LastRequest { get; private set; }

    public Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken ct)
    {
        LastRequest = request;
        var results = new Dictionary<string, FormatGenerationResult<object>>(StringComparer.OrdinalIgnoreCase)
        {
            [formatKey] = FormatGenerationResult<object>.Succeeded(payload, "fake.v1", [])
        };
        return Task.FromResult(new OrchestratorResult(OrchestratorStatus.Succeeded, results, brand));
    }
}

// ── Infrastructure stubs ─────────────────────────────────────────────────────

file sealed class AwWiringStubJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class AwWiringStubAnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
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

file sealed class AwWiringStubFormatResultRepository : IContentGenerationFormatResultRepository
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

file sealed class AwWiringStubOrchestrator(string formatKey, object payload, BrandSnapshot? brand = null) : IContentGenerationOrchestrator
{
    public Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken ct)
    {
        var results = new Dictionary<string, FormatGenerationResult<object>>(StringComparer.OrdinalIgnoreCase)
        {
            [formatKey] = FormatGenerationResult<object>.Succeeded(payload, "fake.v1", [])
        };
        return Task.FromResult(new OrchestratorResult(
            OrchestratorStatus.Succeeded,
            results,
            brand ?? BrandSnapshot.FromBrandData(null, null, null)));
    }
}

file sealed class AwWiringStaticBrandLoader(BrandSnapshot brand) : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(brand);
}

file sealed class AwWiringSimpleComposer : IVisualCompositionService
{
    private static readonly CompositionPalette Palette = new("#111", "#222", "#333", "#FFF", "#EEE");
    private static readonly CompositionTypography Typo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions Dim = new(1080, 1080, 96);

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct) =>
        Task.FromResult(BuildSpec("slide-1"));

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, CarouselBackgroundPlan? plan = null) =>
        Task.FromResult<IReadOnlyList<VisualCompositionSpec>>([BuildSpec("slide-1")]);

    public IReadOnlyList<string> GetInferredFields() => [];

    private static VisualCompositionSpec BuildSpec(string slotKey) =>
        new(FormatKey: "carousel",
            LayoutType: "grid",
            Palette: Palette,
            Typography: Typo,
            AssetSlots: new Dictionary<string, AssetSlotSpec> { [slotKey] = new AssetSlotSpec(null, "bg") },
            CopySlots: new Dictionary<string, string> { ["title"] = "T", ["subtitle"] = "S", ["cta"] = "C" },
            Dimensions: Dim);
}

file sealed class AwWiringSimpleRenderer : IContentRenderer
{
    private static readonly byte[] FakePngBytes = [0x89, 0x50, 0x4E, 0x47];
    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct) =>
        Task.FromResult(Result.Success(RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", FakePngBytes)));
}

file sealed class AwWiringStubPackageBuilder : IGeneratedPackageBuilder
{
    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-zip"));
        var manifest = new GeneratedPackageManifest(new Dictionary<string, FormatAssetMetadata>(), [], [], 0, input.ClientName ?? "test");
        return Task.FromResult(new BuiltPackage(stream, stream.Length, manifest, "test.zip"));
    }
}

file sealed class AwWiringStubPackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class AwWiringStubPackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) => Task.FromResult<GeneratedPackage?>(null);
}
