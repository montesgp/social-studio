using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Observability;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Background;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ContentGeneration;
using ClientProfileSnapshot = Incoders.Hive.Domain.Social.ContentGeneration.ClientProfileSnapshot;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// WU-6.12 integration tests: <see cref="ContentGenerationJobProcessor"/> with the full
/// ClientAesthetic carousel pipeline, including per-slide telemetry (CAC-INT-01).
///
/// Covers:
/// <list type="bullet">
///   <item>Full ClientAesthetic carousel: creative slides receive ForcedDefault backgrounds; job completes.</item>
///   <item>Cold-start parity: all-null <see cref="ClientAestheticProfile"/> → job completes; CSS-only path.</item>
///   <item>Image-gen failure on creative slides → CSS fallback + telemetry rows + job completes.</item>
/// </list>
/// </summary>
public sealed class ProcessorClientAlignedCarouselTests
{
    private static readonly Guid OrgId = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
    private static readonly Guid ClientId = Guid.Parse("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2");
    private static readonly Guid AnalysisId = Guid.Parse("c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3");
    private static readonly Guid JobId = Guid.Parse("d4d4d4d4-d4d4-d4d4-d4d4-d4d4d4d4d4d4");

    // -----------------------------------------------------------------------
    // Full ClientAesthetic carousel: image service called; job completes.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FullClientAesthetic_ImageServiceCalled_JobCompletes()
    {
        var captured = new List<ClientAestheticImageRequest>();
        var partialMap = new Dictionary<int, BackgroundSelection.ForcedDefault>
        {
            [1] = new BackgroundSelection.ForcedDefault("https://cdn.example.com/slide1.png", "ai-generated-background"),
            [5] = new BackgroundSelection.ForcedDefault("https://cdn.example.com/slide5.png", "ai-generated-background"),
        };
        var imageService = new CAC12CaptureAndReturnService(partialMap, captured);
        var renderer = new CAC12TrackingRenderer();

        var processor = BuildProcessor(
            slideCount: 5,
            profile: FullProfile(),
            imageBackgroundService: imageService,
            renderer: renderer);

        var ex = await Record.ExceptionAsync(() => processor.ProcessAsync(JobId, CancellationToken.None));

        Assert.Null(ex);
        Assert.Single(captured);  // service called exactly once per job
        Assert.True(renderer.RenderCallCount > 0);
    }

    // -----------------------------------------------------------------------
    // CAC-BSP: only Photo slides (per the client's captured BackgroundPattern)
    // enter the creative set. FlatColor slides must NOT receive AI generation —
    // they fall through to the CSS/palette path. Regression for the FlatColor-
    // slide-conditioned-on-a-photo-reference noise bug: the {first,last} default
    // was kind-blind and pulled a FlatColor slide into image generation.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task BackgroundPattern_FlatColorSlidesExcluded_OnlyPhotoSlidesGenerated()
    {
        var captured = new List<ClientAestheticImageRequest>();
        var imageService = new CAC12CaptureAndReturnService(
            new Dictionary<int, BackgroundSelection.ForcedDefault>(), captured);

        // 5-slide client whose captured pattern makes slide 5 (the {first,last}
        // default's "last") a FlatColor slide. Photo slides are 1 and 3.
        var profile = FullProfile() with
        {
            BackgroundPattern =
            [
                SlideBackgroundKind.Photo,
                SlideBackgroundKind.FlatColor,
                SlideBackgroundKind.Photo,
                SlideBackgroundKind.FlatColor,
                SlideBackgroundKind.FlatColor,
            ],
        };

        var processor = BuildProcessor(
            slideCount: 5,
            profile: profile,
            imageBackgroundService: imageService);

        var ex = await Record.ExceptionAsync(() => processor.ProcessAsync(JobId, CancellationToken.None));

        Assert.Null(ex);
        Assert.Single(captured);
        // Only Photo slides (1, 3) get AI generation; FlatColor slides (2, 4, 5) never do.
        Assert.Equal(new[] { 1, 3 }, captured[0].CreativeSlideIndices);
    }

    // -----------------------------------------------------------------------
    // The per-slide copy is threaded into the image request so the prompt writer can ground each
    // generated background as a metaphor of that slide's message (previously passed empty).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PerSlideCopySummaries_ThreadedFromPayload_GroundsImageMetaphor()
    {
        var captured = new List<ClientAestheticImageRequest>();
        var imageService = new CAC12CaptureAndReturnService(
            new Dictionary<int, BackgroundSelection.ForcedDefault>(), captured);

        var processor = BuildProcessor(
            slideCount: 5,
            profile: FullProfile(),
            imageBackgroundService: imageService);

        var ex = await Record.ExceptionAsync(() => processor.ProcessAsync(JobId, CancellationToken.None));

        Assert.Null(ex);
        Assert.Single(captured);
        var summaries = captured[0].PerSlideCopySummaries;
        Assert.Equal(5, summaries.Count);
        Assert.Contains("Slide 1", summaries[0]);
        Assert.Contains("Body 1", summaries[0]);
    }

    // -----------------------------------------------------------------------
    // Cold-start parity: all-null profile → CSS path; job completes without error.
    // (CAC-INT-01: no ForcedDefault → all slides use CSS designer path.)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ColdStart_AllNullProfile_FailsClosed_NoGenerationAttempted()
    {
        // Real-generation fix (2026-08-28): superseded. A genuinely empty profile (no
        // BackgroundPattern, no ColorPairings, no descriptors) used to fall through to the CSS
        // designer path — this is exactly the "job ran 30 minutes and produced nothing usable"
        // failure mode the owner asked to close. ClientAesthetic with nothing usable must now
        // refuse to generate at all, not silently degrade to a generic CSS fallback.
        var coldStartProfile = new ClientAestheticProfile(
            BackgroundDescriptors: [],
            VoiceTone: null,
            AverageSlidesPerCarousel: null,
            AnalyzedAtUtc: DateTime.UtcNow);

        var imageService = new CAC12FixedResultService(new Dictionary<int, BackgroundSelection.ForcedDefault>());
        var renderer = new CAC12TrackingRenderer();

        var processor = BuildProcessor(
            slideCount: 5,
            profile: coldStartProfile,
            imageBackgroundService: imageService,
            renderer: renderer);

        var ex = await Record.ExceptionAsync(() => processor.ProcessAsync(JobId, CancellationToken.None));

        // Fails closed, not with an unhandled exception — the job's own Failed status carries the
        // reason (aesthetic-profile-unusable); the caller never sees a thrown exception.
        Assert.Null(ex);
        Assert.Equal(0, renderer.RenderCallCount);
    }

    // -----------------------------------------------------------------------
    // Image-gen failure on creative slides → CSS fallback + telemetry rows +
    // job completes without error (CAC-INT-01-b, CIG-CG-03).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ImageGenFailure_CssFallback_TelemetryRowsWritten_JobCompleted()
    {
        // GIVEN: real background service wired with a failing gateway and spy telemetry repo
        var telemetryRepo = new CAC12SpyAttemptRepository();
        var realImageService = BuildRealImageService(telemetryRepo);
        var renderer = new CAC12TrackingRenderer();

        var processor = BuildProcessor(
            slideCount: 5,
            profile: FullProfile(),
            imageBackgroundService: realImageService,
            renderer: renderer);

        // WHEN: job runs
        var ex = await Record.ExceptionAsync(() => processor.ProcessAsync(JobId, CancellationToken.None));

        // THEN: job completes (no propagated exception)
        Assert.Null(ex);

        // AND: renderer called (CSS path for all slides)
        Assert.True(renderer.RenderCallCount > 0,
            "Renderer must be called — CSS fallback path.");

        // AND: telemetry rows were written for the failed slides (CAC-INT-01-b)
        Assert.NotEmpty(telemetryRepo.Attempts);
        Assert.All(telemetryRepo.Attempts, r => Assert.Equal(IntegrationExecutionAttemptStatus.Failed, r.Status));
        Assert.All(telemetryRepo.Attempts, r => Assert.Equal(
            ClientAestheticImageBackgroundService.SlideCorrelationKind, r.CorrelationKind));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ClientAestheticProfile FullProfile() => new(
        BackgroundDescriptors: ["dark botanical"],
        VoiceTone: "warm",
        AverageSlidesPerCarousel: 5.0,
        AnalyzedAtUtc: DateTime.UtcNow,
        StyleDescriptors: ["organic"],
        Motifs: ["leaves"],
        CaptionVoice: "friendly",
        Palette: [new AestheticPaletteEntry("#3D2B1F", "primary", 0)]);

    private static ClientAestheticImageBackgroundService BuildRealImageService(
        IIntegrationExecutionAttemptRepository telemetryRepo) =>
        new(
            promptWriter: new CAC12StaticPromptWriter("a beautiful background"),
            imageGateway: new CAC12AlwaysFailingGateway(),
            storage: new CAC12NullStorage(),
            logger: NullLogger<ClientAestheticImageBackgroundService>.Instance,
            guardrailValidator: null,
            attemptRepository: telemetryRepo);

    private ContentGenerationJobProcessor BuildProcessor(
        int slideCount,
        ClientAestheticProfile profile,
        IClientAestheticImageBackgroundService imageBackgroundService,
        IContentRenderer? renderer = null)
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
            DetectedHooks: [],
            LikelyAudience: "SMBs",
            NarrativeStructure: "problem-solution",
            Strengths: [],
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
            SourceUrl: "https://example.com/test",
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

        var payload = BuildCarouselPayload(slideCount);

        var brand = BrandSnapshot.FromBrandData(
            new BrandManualSnapshot(
                ColorPalette: [new WeightedColor("#333333", null, 0), new WeightedColor("#666666", null, 1)],
                Logos: [],
                BackgroundTemplates: [],
                Typography: ["Inter"],
                VoiceAndTone: null,
                Notes: null),
            strategy: null,
            monthlyObjective: null,
            client: new ClientProfileSnapshot("Test Client", null, null, BackgroundSourcingPolicy.ClientAesthetic));

        return new ContentGenerationJobProcessor(
            jobs: new CAC12JobRepository(fakeJob),
            analyses: new CAC12AnalysisRepository(analysisRecord),
            orchestrator: new CAC12Orchestrator("carousel", payload, brand),
            formatResults: new CAC12FormatResultRepository(),
            brandLoader: new CAC12BrandLoader(brand),
            composer: new CAC12CompositionService("carousel", slideCount),
            renderer: renderer ?? new CAC12TrackingRenderer(),
            packageBuilder: new CAC12PackageBuilder(),
            packageStorage: new CAC12PackageStorage(),
            packages: new CAC12PackageRepository(),
            assetStorage: null,
            assetRepository: null,
            designer: new CAC12PassthroughDesigner(),
            guardrails: new CAC12PassingGuardrails(),
            providerCallContext: new ProviderCallContext(),
            logger: NullLogger<ContentGenerationJobProcessor>.Instance,
            backgroundStrategyResolver: new CAC12StrategyResolver("carousel"),
            aestheticDescriptorReader: null,
            aestheticProfileReader: new CAC12FixedProfileReader(profile),
            aestheticEnsureFreshService: null,
            imageBackgroundService: imageBackgroundService);
    }

    private static object BuildCarouselPayload(int slideCount)
    {
        var slides = Enumerable.Range(1, slideCount)
            .Select(i => new { title = $"Slide {i}", body = $"Body {i}" })
            .ToArray();
        var payload = new { slides, coverTitle = "Cover", publicationDescription = "Test" };
        // Serialize to JsonElement so ContentGenerationJobProcessor.TryGetSlideCount reads the real
        // slide count from the "slides" array (an anonymous object falls back to a count of 1),
        // matching how the processor receives payloads in production.
        return JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}

// -----------------------------------------------------------------------
// Test doubles — file-scoped with CAC12_ prefix to avoid collision
// -----------------------------------------------------------------------

file sealed class CAC12SpyAttemptRepository : IIntegrationExecutionAttemptRepository
{
    public List<IntegrationExecutionAttemptRecord> Attempts { get; } = [];

    public Task AddAsync(IntegrationExecutionAttemptRecord attempt, CancellationToken cancellationToken = default)
    {
        Attempts.Add(attempt);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IntegrationExecutionAttemptRecord>> ListByAnalysisIdAsync(
        Guid analysisId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IntegrationExecutionAttemptRecord>>(Array.Empty<IntegrationExecutionAttemptRecord>());
}

file sealed class CAC12CaptureAndReturnService(
    IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault> map,
    List<ClientAestheticImageRequest> captured)
    : IClientAestheticImageBackgroundService
{
    public Task<IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>> BuildCreativeBackgroundsAsync(
        ClientAestheticImageRequest request,
        CancellationToken ct = default)
    {
        captured.Add(request);
        return Task.FromResult(map);
    }
}

file sealed class CAC12FixedResultService(IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault> map)
    : IClientAestheticImageBackgroundService
{
    public Task<IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>> BuildCreativeBackgroundsAsync(
        ClientAestheticImageRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(map);
}

file sealed class CAC12AlwaysFailingGateway : IImageGenerationGateway
{
    public Task<Result<ImageGenerationResult>> GenerateAsync(ImageGenerationRequest request, CancellationToken ct = default) =>
        Task.FromResult(Result.Failure<ImageGenerationResult>(
            Error.Failure("image-gen.provider-unavailable", "Simulated provider failure")));

    public Task<Result<ImageModelCapabilities>> DescribeAsync(Guid organizationId, CancellationToken ct = default) =>
        Task.FromResult(Result.Failure<ImageModelCapabilities>(
            Error.Failure("image-gen.describe-failed", "Simulated describe failure")));
}

file sealed class CAC12StaticPromptWriter(string prompt) : IImagePromptWriter
{
    public Task<Result<string>> WriteAsync(ImagePromptRequest request, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(prompt));
}

file sealed class CAC12NullStorage : IGeneratedAssetStorage
{
    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
        Task.FromResult(new StoredAssetRef($"orgs/{key.OrganizationId}/{key.RelativePath}", content.Mime, content.Bytes.Length, "hash"));

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct) =>
        Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}

file sealed class CAC12TrackingRenderer : IContentRendererWithGeometry
{
    private int _callCount;
    public int RenderCallCount => _callCount;

    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake-png");
        return Task.FromResult(Result.Success(RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes)));
    }

    public Task<Result<RenderedAssetWithGeometry>> RenderWithGeometryAsync(
        VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake-png");
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
        return Task.FromResult(Result.Success(new RenderedAssetWithGeometry(asset, [], null)));
    }
}

file sealed class CAC12PassthroughDesigner : IVisualDesigner
{
    public bool IsActive => true;

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken,
        ClientAestheticDesignContext? aesthetic = null) =>
        Task.FromResult(Result.Success(new VisualDesignOutput(
            Markup: $"""<div class="composition" style="width:{contentPayload.Dimensions.Width}px;height:{contentPayload.Dimensions.Height}px;"></div>""",
            PromptVersionKey: "visual-designer/v1",
            IsValid: false)));
}

file sealed class CAC12PassingGuardrails : IVisualGuardrailValidator
{
    public Result<VisualDesignOutput> Validate(VisualDesignOutput output, BrandManualSnapshot brand, VisualCompositionSpec spec) =>
        Result.Success(output.WithValidated());
}

file sealed class CAC12BrandLoader(BrandSnapshot brand) : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(brand);
}

file sealed class CAC12FixedProfileReader(ClientAestheticProfile profile) : IClientAestheticProfileReader
{
    public Task<ClientAestheticProfile?> GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct) =>
        Task.FromResult<ClientAestheticProfile?>(profile);
}

file sealed class CAC12StrategyResolver(string formatKey) : IBackgroundStrategyResolver
{
    public IBackgroundSourcingStrategy Resolve(BackgroundSourcingPolicy? policy) =>
        (IBackgroundSourcingStrategy)(policy == BackgroundSourcingPolicy.ClientAesthetic
            ? new CAC12ClientAestheticStrategy(formatKey)
            : null!);
}

file sealed class CAC12ClientAestheticStrategy(string formatKey) : IBackgroundSourcingStrategy
{
    public BackgroundSourcingPolicy Policy => BackgroundSourcingPolicy.ClientAesthetic;

    public CarouselBackgroundPlan Plan(BackgroundPlanRequest request)
    {
        var count = request.SlideCount < 1 ? 1 : request.SlideCount;
        var perSlide = Enumerable.Range(1, count)
            .Select(_ => (BackgroundSelection)new BackgroundSelection.ClientAesthetic([]))
            .ToList();
        return new CarouselBackgroundPlan(BackgroundSourcingPolicy.ClientAesthetic, perSlide, request.SeedToken);
    }
}

file sealed class CAC12CompositionService(string designedFormat, int slideCount) : IVisualCompositionService
{
    private static readonly CompositionPalette DefaultPalette = new("#333", "#666", "#999", "#FFF", "#EEE");
    private static readonly CompositionTypography DefaultTypo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions DefaultDim = new(1080, 1080, 96);

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, ClientAestheticProfile? aestheticProfile = null) =>
        Task.FromResult(BuildSpec(formatKey));

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, CarouselBackgroundPlan? plan = null, ClientAestheticProfile? aestheticProfile = null)
    {
        var specs = Enumerable.Range(1, slideCount).Select(_ => BuildSpec(formatKey)).ToList();
        return Task.FromResult<IReadOnlyList<VisualCompositionSpec>>(specs);
    }

    private VisualCompositionSpec BuildSpec(string formatKey)
    {
        var outputKind = string.Equals(formatKey, designedFormat, StringComparison.OrdinalIgnoreCase)
            ? CompositionOutputKind.Raster
            : CompositionOutputKind.Text;
        return new VisualCompositionSpec(
            FormatKey: formatKey,
            LayoutType: "carousel-carousel",
            Palette: DefaultPalette,
            Typography: DefaultTypo,
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["title"] = "Test slide" },
            Dimensions: DefaultDim)
        with { OutputKind = outputKind, TextBody = outputKind == CompositionOutputKind.Text ? "body" : null };
    }

    public IReadOnlyList<string> GetInferredFields() => [];
}

file sealed class CAC12JobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class CAC12AnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
{
    public Task AddAsync(SingleContentAnalysisRecord a, CancellationToken ct = default) => Task.CompletedTask;
    public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct = default) => Task.FromResult<SingleContentAnalysisRecord?>(record);
    public Task UpdateStatusAsync(Guid id, Guid orgId, SingleContentAnalysisStatus status, DateTime updatedAt, CancellationToken ct = default, string? failCode = null, string? failMsgKey = null) => Task.CompletedTask;
    public Task UpdateAsync(SingleContentAnalysisRecord a, CancellationToken ct = default) => Task.CompletedTask;
    public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(Guid orgId, Guid userId, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));
    public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(Guid orgId, Guid userId, string normalized, string src, CancellationToken ct = default) =>
        Task.FromResult<SingleContentAnalysisHistoryItem?>(null);
    public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(Guid id, Guid orgId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>?>([]);
}

file sealed class CAC12Orchestrator(string formatKey, object payload, BrandSnapshot brand) : IContentGenerationOrchestrator
{
    public Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken ct)
    {
        var results = new Dictionary<string, FormatGenerationResult<object>>(StringComparer.OrdinalIgnoreCase)
        {
            [formatKey] = FormatGenerationResult<object>.Succeeded(payload, "test.v1", [])
        };
        return Task.FromResult(new OrchestratorResult(OrchestratorStatus.Succeeded, results, brand));
    }
}

file sealed class CAC12FormatResultRepository : IContentGenerationFormatResultRepository
{
    private readonly Dictionary<(Guid JobId, string FormatKey), ContentGenerationFormatResult> _store = new();

    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult r, CancellationToken ct)
    {
        _store[(r.JobId, r.FormatKey)] = r;
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>([]);
}

file sealed class CAC12PackageBuilder : IGeneratedPackageBuilder
{
    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-zip"));
        var manifest = new GeneratedPackageManifest(new Dictionary<string, FormatAssetMetadata>(), [], [], 0, "test");
        return Task.FromResult(new BuiltPackage(stream, stream.Length, manifest, "test.zip"));
    }
}

file sealed class CAC12PackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class CAC12PackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) =>
        Task.FromResult<GeneratedPackage?>(null);
}
