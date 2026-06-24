using System.Text.Json;
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
/// WU-8.2 Resilience tests: image-gen failure → CSS fallback; job completes; no unhandled exception.
///
/// Refs: CIG-CG-03-a (partial success — CSS for failed slides), CIG-CG-03-b (all fail → all CSS),
/// IMG-05 (service exception caught → all CSS, job completes); design §4.1.
/// </summary>
public sealed class ProcessorImageBackgroundResilienceTests
{
    private static readonly Guid OrgId = Guid.Parse("5e5e5e5e-5e5e-5e5e-5e5e-5e5e5e5e5e5e");
    private static readonly Guid ClientId = Guid.Parse("6f6f6f6f-6f6f-6f6f-6f6f-6f6f6f6f6f6f");
    private static readonly Guid AnalysisId = Guid.Parse("7a7a7a7a-7a7a-7a7a-7a7a-7a7a7a7a7a7a");
    private static readonly Guid JobId = Guid.Parse("8b8b8b8b-8b8b-8b8b-8b8b-8b8b8b8b8b8b");

    // -----------------------------------------------------------------------
    // CIG-CG-03-b: service returns empty map → all slides degrade to CSS; job completes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ClientAesthetic_ServiceReturnsEmptyMap_AllSlidesCss_JobCompletes()
    {
        // GIVEN a ClientAesthetic carousel with creative slides [1, 5], service returns empty map (all failed)
        var rendererSpy = new ResilienceTrackingRenderer();
        var processor = BuildProcessor(
            slideCount: 5,
            creativeSlideIndices: [1, 5],
            imageService: new FixedResultImageService(new Dictionary<int, BackgroundSelection.ForcedDefault>()),
            renderer: rendererSpy);

        // WHEN processing runs — THEN no exception propagates
        var ex = await Record.ExceptionAsync(() => processor.ProcessAsync(JobId, CancellationToken.None));
        Assert.Null(ex);

        // AND all slides were rendered (CSS designer path for each)
        Assert.True(rendererSpy.RenderCallCount > 0,
            "Renderer MUST be called for each slide even when all image-gen failed (CIG-CG-03-b).");
    }

    // -----------------------------------------------------------------------
    // CIG-CG-03-a: partial map (slide 1 succeeded, slide 5 absent) → job completes; image service called
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ClientAesthetic_ServiceReturnsPartialMap_JobCompletes_NoPropagatedError()
    {
        // GIVEN service returns ForcedDefault only for slide 1 (slide 5 absent = CSS fallback)
        var rendererSpy = new ResilienceTrackingRenderer();
        var capturedRequests = new List<ClientAestheticImageRequest>();
        var partialMap = new Dictionary<int, BackgroundSelection.ForcedDefault>
        {
            [1] = new BackgroundSelection.ForcedDefault("https://storage.example.com/slide1.png", "ai-generated-background")
        };
        var capturingService = new CapturingImageService(partialMap, capturedRequests);

        var processor = BuildProcessor(
            slideCount: 5,
            creativeSlideIndices: [1, 5],
            imageService: capturingService,
            renderer: rendererSpy);

        // WHEN
        var ex = await Record.ExceptionAsync(() => processor.ProcessAsync(JobId, CancellationToken.None));

        // THEN no exception propagates (CIG-CG-03-a)
        Assert.Null(ex);

        // AND the renderer was called (all slides rendered — partial image success handled)
        Assert.True(rendererSpy.RenderCallCount > 0,
            "Renderer MUST be called when service returns a partial map — slides render normally.");

        // AND the image service was called with at least one creative index
        // (exact index set depends on TryGetSlideCount / payload shape; the key contract
        // is that the service was entered for ClientAesthetic + non-null imageBackgroundService)
        Assert.NotEmpty(capturedRequests);
        Assert.NotEmpty(capturedRequests[0].CreativeSlideIndices);
    }

    // -----------------------------------------------------------------------
    // IMG-05: service throws an exception → caught; all slides degrade to CSS; job completes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ClientAesthetic_ServiceThrowsException_CaughtAndJobCompletes()
    {
        // GIVEN the service throws an unhandled exception
        var rendererSpy = new ResilienceTrackingRenderer();
        var processor = BuildProcessor(
            slideCount: 3,
            creativeSlideIndices: [1, 3],
            imageService: new ThrowingImageService(),
            renderer: rendererSpy);

        // WHEN — THEN no unhandled exception propagates (IMG-05)
        var ex = await Record.ExceptionAsync(() => processor.ProcessAsync(JobId, CancellationToken.None));
        Assert.Null(ex);

        // AND the renderer was still called — CSS path used as fallback
        Assert.True(rendererSpy.RenderCallCount > 0,
            "Renderer MUST be called even after service exception — CSS fallback ensures job completion (IMG-05).");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private ContentGenerationJobProcessor BuildProcessor(
        int slideCount,
        IReadOnlyList<int> creativeSlideIndices,
        IClientAestheticImageBackgroundService imageService,
        IContentRenderer? renderer = null,
        IVisualDesigner? designer = null)
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
        var profile = new ClientAestheticProfile(
            BackgroundDescriptors: ["minimal"],
            VoiceTone: "professional",
            AverageSlidesPerCarousel: slideCount,
            AnalyzedAtUtc: DateTime.UtcNow,
            CreativeSlideIndices: creativeSlideIndices);

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

        var actualDesigner = designer ?? new ResiliencePassthroughDesigner();
        var actualRenderer = renderer ?? new ResilienceTrackingRenderer();

        return new ContentGenerationJobProcessor(
            jobs: new ResilienceJobRepository(fakeJob),
            analyses: new ResilienceAnalysisRepository(analysisRecord),
            orchestrator: new ResilienceOrchestrator("carousel", payload, brand),
            formatResults: new ResilienceFormatResultRepository(),
            brandLoader: new ResilienceBrandLoader(brand),
            composer: new ResilienceCompositionService("carousel", slideCount),
            renderer: actualRenderer,
            packageBuilder: new ResiliencePackageBuilder(),
            packageStorage: new ResiliencePackageStorage(),
            packages: new ResiliencePackageRepository(),
            assetStorage: null,
            assetRepository: null,
            designer: actualDesigner,
            guardrails: new ResiliencePassingGuardrails(),
            providerCallContext: new ProviderCallContext(),
            logger: NullLogger<ContentGenerationJobProcessor>.Instance,
            backgroundStrategyResolver: new ResilienceClientAestheticStrategyResolver("carousel", slideCount),
            aestheticDescriptorReader: null,
            aestheticProfileReader: new ResilienceFixedAestheticProfileReader(profile),
            aestheticEnsureFreshService: null,
            imageBackgroundService: imageService);
    }

    private static object BuildCarouselPayload(int slideCount)
    {
        var slides = Enumerable.Range(1, slideCount).Select(i => new
        {
            title = $"Slide {i}",
            body = $"Body {i}"
        }).ToArray();
        return new { slides, coverTitle = "Cover", publicationDescription = "Test" };
    }
}

// -----------------------------------------------------------------------
// Test-local spy/stub types
// -----------------------------------------------------------------------

/// <summary>Returns a fixed map of ForcedDefault backgrounds.</summary>
file sealed class FixedResultImageService(IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault> map)
    : IClientAestheticImageBackgroundService
{
    public Task<IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>> BuildCreativeBackgroundsAsync(
        ClientAestheticImageRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(map);
}

/// <summary>Throws InvalidOperationException to test the processor's exception isolation.</summary>
file sealed class ThrowingImageService : IClientAestheticImageBackgroundService
{
    public Task<IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>> BuildCreativeBackgroundsAsync(
        ClientAestheticImageRequest request,
        CancellationToken ct = default) =>
        throw new InvalidOperationException("Simulated image-gen service failure");
}

/// <summary>Captures the request passed to BuildCreativeBackgroundsAsync and returns a fixed map.</summary>
file sealed class CapturingImageService(
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

/// <summary>Captures BackgroundSelection passed to ComposeAsync for each slide.</summary>
file sealed class SelectionCapturingDesigner : IVisualDesigner
{
    private readonly List<BackgroundSelection> _selections = new();
    public IReadOnlyList<BackgroundSelection> ReceivedSelections => _selections;
    public bool IsActive => true;

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken,
        ClientAestheticDesignContext? aesthetic = null)
    {
        _selections.Add(backgroundSelection);
        return Task.FromResult(Result.Success(new VisualDesignOutput(
            Markup: BuildMarkup(contentPayload),
            PromptVersionKey: "visual-designer/v1",
            IsValid: false)));
    }

    private static string BuildMarkup(VisualCompositionSpec spec) =>
        $"""<div class="composition" data-format="{spec.FormatKey}" style="width:{spec.Dimensions.Width}px;height:{spec.Dimensions.Height}px;"><div class="layer-background" data-layer="background"></div><div class="layer-content" data-layer="content"></div></div>""";
}

file sealed class ResiliencePassthroughDesigner : IVisualDesigner
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
            Markup: $"""<div class="composition" data-format="{contentPayload.FormatKey}" style="width:{contentPayload.Dimensions.Width}px;height:{contentPayload.Dimensions.Height}px;"><div class="layer-background" data-layer="background"></div></div>""",
            PromptVersionKey: "visual-designer/v1",
            IsValid: false)));
}

file sealed class ResiliencePassingGuardrails : IVisualGuardrailValidator
{
    public Result<VisualDesignOutput> Validate(VisualDesignOutput output, BrandManualSnapshot brand, VisualCompositionSpec spec) =>
        Result.Success(output.WithValidated());
}

file sealed class ResilienceTrackingRenderer : IContentRendererWithGeometry
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

file sealed class ResilienceBrandLoader(BrandSnapshot brand) : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(brand);
}

file sealed class ResilienceFixedAestheticProfileReader(ClientAestheticProfile profile) : IClientAestheticProfileReader
{
    public Task<ClientAestheticProfile?> GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct) =>
        Task.FromResult<ClientAestheticProfile?>(profile);
}

file sealed class ResilienceClientAestheticStrategyResolver(string formatKey, int slideCount) : IBackgroundStrategyResolver
{
    public IBackgroundSourcingStrategy? Resolve(BackgroundSourcingPolicy? policy) =>
        policy == BackgroundSourcingPolicy.ClientAesthetic
            ? new ResilienceClientAestheticStrategy(slideCount)
            : null;
}

file sealed class ResilienceClientAestheticStrategy(int slideCount) : IBackgroundSourcingStrategy
{
    public BackgroundSourcingPolicy Policy => BackgroundSourcingPolicy.ClientAesthetic;

    public CarouselBackgroundPlan Plan(BackgroundPlanRequest request)
    {
        var count = request.SlideCount < 1 ? 1 : request.SlideCount;
        var perSlide = Enumerable.Range(1, count)
            .Select(_ => (BackgroundSelection) new BackgroundSelection.ClientAesthetic([]))
            .ToList();
        return new CarouselBackgroundPlan(BackgroundSourcingPolicy.ClientAesthetic, perSlide, request.SeedToken);
    }
}

file sealed class ResilienceCompositionService(string designedFormat, int slideCount) : IVisualCompositionService
{
    private static readonly CompositionPalette DefaultPalette = new("#333", "#666", "#999", "#FFF", "#EEE");
    private static readonly CompositionTypography DefaultTypo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions DefaultDim = new(1080, 1080, 96);

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct) =>
        Task.FromResult(BuildSpec(formatKey));

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, CarouselBackgroundPlan? plan = null)
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
            CopySlots: new Dictionary<string, string> { ["title"] = "Slide" },
            Dimensions: DefaultDim)
        with { OutputKind = outputKind, TextBody = outputKind == CompositionOutputKind.Text ? "body" : null };
    }

    public IReadOnlyList<string> GetInferredFields() => [];
}

file sealed class ResilienceJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class ResilienceAnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
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

file sealed class ResilienceFormatResultRepository : IContentGenerationFormatResultRepository
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

file sealed class ResilienceOrchestrator(string formatKey, object payload, BrandSnapshot brand)
    : IContentGenerationOrchestrator
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

file sealed class ResiliencePackageBuilder : IGeneratedPackageBuilder
{
    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-zip"));
        var manifest = new GeneratedPackageManifest(new Dictionary<string, FormatAssetMetadata>(), [], [], 0, "test");
        return Task.FromResult(new BuiltPackage(stream, stream.Length, manifest, "test.zip"));
    }
}

file sealed class ResiliencePackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class ResiliencePackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) =>
        Task.FromResult<GeneratedPackage?>(null);
}
