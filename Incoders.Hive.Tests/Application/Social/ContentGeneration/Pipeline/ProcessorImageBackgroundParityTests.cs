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
/// WU-8.1 Parity tests: the existing CSS designer path MUST remain byte-for-byte unchanged
/// when <see cref="IClientAestheticImageBackgroundService"/> is null (absent) or when the
/// client policy is not ClientAesthetic.
///
/// Refs: CIG-CG-01-b (no image service → all CSS), CIG-CG-01-c (non-ClientAesthetic unchanged);
/// IMG-06-a (no route configured → CSS silently); design §4.1.
/// </summary>
public sealed class ProcessorImageBackgroundParityTests
{
    private static readonly Guid OrgId = Guid.Parse("1a1a1a1a-1a1a-1a1a-1a1a-1a1a1a1a1a1a");
    private static readonly Guid ClientId = Guid.Parse("2b2b2b2b-2b2b-2b2b-2b2b-2b2b2b2b2b2b");
    private static readonly Guid AnalysisId = Guid.Parse("3c3c3c3c-3c3c-3c3c-3c3c-3c3c3c3c3c3c");
    private static readonly Guid JobId = Guid.Parse("4d4d4d4d-4d4d-4d4d-4d4d-4d4d4d4d4d4d");

    // -----------------------------------------------------------------------
    // CIG-CG-01-b: ClientAesthetic + no image service → CSS path only
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ClientAesthetic_NoImageService_AllSlidesUseCssDesigner()
    {
        // GIVEN a ClientAesthetic carousel with NO IClientAestheticImageBackgroundService registered
        var imageServiceSpy = new ImageServiceCallSpy();
        var rendererSpy = new ParityRenderer();

        var processor = BuildProcessor(
            formatKey: "carousel",
            payload: BuildCarouselPayload(slideCount: 3),
            policy: BackgroundSourcingPolicy.ClientAesthetic,
            imageBackgroundService: null,      // <-- absent: CSS path only
            aestheticProfile: BuildAestheticProfile(creativeSlideIndices: [1, 3]),
            renderer: rendererSpy);

        // WHEN
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN BuildCreativeBackgroundsAsync is NEVER called (CIG-CG-01-b / IMG-06-a)
        Assert.False(imageServiceSpy.WasCalled,
            "BuildCreativeBackgroundsAsync MUST NOT be called when the service is null — CSS passthrough.");

        // AND the renderer IS called (slides were rendered — CSS path completed normally)
        Assert.True(rendererSpy.WasCalled,
            "Renderer MUST be called — carousel slides should complete via the CSS legacy render path.");
    }

    // -----------------------------------------------------------------------
    // CIG-CG-01-c: non-ClientAesthetic policy → image path never entered
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NonClientAesthetic_Policy_ImagePathNeverEntered()
    {
        // GIVEN a carousel job with AiPalette policy (NOT ClientAesthetic)
        var imageServiceSpy = new ImageServiceCallSpy();
        var designerSpy = new TrackingDesigner();

        var processor = BuildProcessor(
            formatKey: "carousel",
            payload: BuildCarouselPayload(slideCount: 3),
            policy: BackgroundSourcingPolicy.AiPalette,
            imageBackgroundService: imageServiceSpy,   // even if wired, must not be called
            aestheticProfile: null,
            designer: designerSpy);

        // WHEN
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN BuildCreativeBackgroundsAsync is NEVER called (CIG-CG-01-c)
        Assert.False(imageServiceSpy.WasCalled,
            "Image background service MUST NOT be called for non-ClientAesthetic policies.");
    }

    [Fact]
    public async Task UnsetPolicy_ImagePathNeverEntered()
    {
        // GIVEN a carousel with null/Unset policy
        var imageServiceSpy = new ImageServiceCallSpy();

        var processor = BuildProcessor(
            formatKey: "carousel",
            payload: BuildCarouselPayload(slideCount: 3),
            policy: null,                              // Unset
            imageBackgroundService: imageServiceSpy,
            aestheticProfile: null,
            designer: new TrackingDesigner());

        // WHEN
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN image service not called
        Assert.False(imageServiceSpy.WasCalled,
            "Image background service MUST NOT be called when policy is Unset.");
    }

    // -----------------------------------------------------------------------
    // FIX-6: non-carousel ClientAesthetic formats → image path never entered
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("story-single")]
    [InlineData("story")]
    [InlineData("feed-single")]
    public async Task ClientAesthetic_NonCarouselFormat_ImagePathNeverEntered(string formatKey)
    {
        // GIVEN a ClientAesthetic job for a non-carousel format (story-single / story / feed-single)
        // with the image background service wired — it must NOT be called (FIX-6).
        var imageServiceSpy = new ImageServiceCallSpy();

        var storyPayload = BuildStoryPayload();
        var aestheticProfile = BuildAestheticProfile(creativeSlideIndices: [1]);

        var processor = BuildProcessor(
            formatKey: formatKey,
            payload: storyPayload,
            policy: BackgroundSourcingPolicy.ClientAesthetic,
            imageBackgroundService: imageServiceSpy,
            aestheticProfile: aestheticProfile,
            designer: new TrackingDesigner());

        // WHEN
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN BuildCreativeBackgroundsAsync is NEVER called (FIX-6: carousel-only guard)
        Assert.False(imageServiceSpy.WasCalled,
            $"Image background service MUST NOT be called for non-carousel format '{formatKey}' " +
            "even when policy is ClientAesthetic — the feature is scoped to carousel layout only.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static object BuildCarouselPayload(int slideCount)
    {
        var slides = Enumerable.Range(1, slideCount).Select(i => new
        {
            title = $"Slide {i} title",
            body = $"Slide {i} body"
        }).ToArray();

        return new { slides, coverTitle = "Cover", publicationDescription = "Test" };
    }

    private static object BuildStoryPayload() =>
        new { title = "Story title", subtitle = "Story subtitle", publicationDescription = "Story test" };

    private static ClientAestheticProfile BuildAestheticProfile(IReadOnlyList<int>? creativeSlideIndices = null) =>
        new(
            BackgroundDescriptors: ["minimal", "clean"],
            VoiceTone: "professional",
            AverageSlidesPerCarousel: 5,
            AnalyzedAtUtc: DateTime.UtcNow,
            CreativeSlideIndices: creativeSlideIndices);

    private ContentGenerationJobProcessor BuildProcessor(
        string formatKey,
        object payload,
        BackgroundSourcingPolicy? policy,
        IClientAestheticImageBackgroundService? imageBackgroundService,
        ClientAestheticProfile? aestheticProfile,
        IVisualDesigner? designer = null,
        IContentRenderer? renderer = null)
    {
        var fakeJob = new ContentGenerationJob(
            Id: JobId,
            OrganizationId: OrgId,
            ClientId: ClientId,
            AnalysisId: AnalysisId,
            RequestedFormats: [formatKey],
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

        var analysisJson = JsonSerializer.Serialize(analysis,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
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
            client: policy is null ? null : new ClientProfileSnapshot("Test Client", null, null, policy));

        var brandLoader = new FixedBrandLoader(brand);
        var activeDesigner = designer ?? new ParityPassthroughDesigner();
        var activeRenderer = renderer ?? new ParityRenderer();
        var aestheticProfileReader = aestheticProfile is not null
            ? (IClientAestheticProfileReader)new FixedAestheticProfileReader(aestheticProfile)
            : new NullAestheticProfileReader();

        return new ContentGenerationJobProcessor(
            jobs: new ParityJobRepository(fakeJob),
            analyses: new ParityAnalysisRepository(analysisRecord),
            orchestrator: new ParityOrchestrator(formatKey, payload, brand),
            formatResults: new ParityFormatResultRepository(),
            brandLoader: brandLoader,
            composer: new ParityRasterCompositionService(formatKey),
            renderer: activeRenderer,
            packageBuilder: new ParityPackageBuilder(),
            packageStorage: new ParityPackageStorage(),
            packages: new ParityPackageRepository(),
            assetStorage: null,
            assetRepository: null,
            designer: activeDesigner,
            guardrails: new ParityPassingGuardrails(),
            providerCallContext: new ProviderCallContext(),
            logger: NullLogger<ContentGenerationJobProcessor>.Instance,
            backgroundStrategyResolver: new ClientAestheticBackgroundStrategyResolver(formatKey),
            aestheticDescriptorReader: null,
            aestheticProfileReader: aestheticProfileReader,
            aestheticEnsureFreshService: null,
            imageBackgroundService: imageBackgroundService);
    }
}

// -----------------------------------------------------------------------
// Spy / stub types (file-scoped so not visible outside this test file)
// -----------------------------------------------------------------------

/// <summary>Spy that tracks whether BuildCreativeBackgroundsAsync was called.</summary>
file sealed class ImageServiceCallSpy : IClientAestheticImageBackgroundService
{
    public bool WasCalled { get; private set; }

    public Task<IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>> BuildCreativeBackgroundsAsync(
        ClientAestheticImageRequest request,
        CancellationToken ct = default)
    {
        WasCalled = true;
        return Task.FromResult<IReadOnlyDictionary<int, BackgroundSelection.ForcedDefault>>(
            new Dictionary<int, BackgroundSelection.ForcedDefault>());
    }
}

/// <summary>Active designer that counts ComposeAsync calls.</summary>
file sealed class TrackingDesigner : IVisualDesigner
{
    public int CallCount { get; private set; }
    public bool IsActive => true;

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken,
        ClientAestheticDesignContext? aesthetic = null)
    {
        CallCount++;
        return Task.FromResult(Result.Success(new VisualDesignOutput(
            Markup: BuildMarkup(contentPayload),
            PromptVersionKey: "visual-designer/v1",
            IsValid: false)));
    }

    private static string BuildMarkup(VisualCompositionSpec spec) =>
        $"""<div class="composition" data-format="{spec.FormatKey}" style="width:{spec.Dimensions.Width}px;height:{spec.Dimensions.Height}px;"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"></div></div>""";
}

/// <summary>Active passthrough designer that always succeeds.</summary>
file sealed class ParityPassthroughDesigner : IVisualDesigner
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
            Markup: """<div class="composition" data-format="carousel" style="width:1080px;height:1080px;"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"></div></div>""",
            PromptVersionKey: "visual-designer/v1",
            IsValid: false)));
}

file sealed class ParityPassingGuardrails : IVisualGuardrailValidator
{
    public Result<VisualDesignOutput> Validate(VisualDesignOutput output, BrandManualSnapshot brand, VisualCompositionSpec spec) =>
        Result.Success(output.WithValidated());
}

file sealed class FixedBrandLoader(BrandSnapshot brand) : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(brand);
}

file sealed class FixedAestheticProfileReader(ClientAestheticProfile profile) : IClientAestheticProfileReader
{
    public Task<ClientAestheticProfile?> GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct) =>
        Task.FromResult<ClientAestheticProfile?>(profile);
}

file sealed class NullAestheticProfileReader : IClientAestheticProfileReader
{
    public Task<ClientAestheticProfile?> GetProfileAsync(Guid orgId, Guid clientId, CancellationToken ct) =>
        Task.FromResult<ClientAestheticProfile?>(null);
}

file sealed class ClientAestheticBackgroundStrategyResolver(string formatKey) : IBackgroundStrategyResolver
{
    public IBackgroundSourcingStrategy? Resolve(BackgroundSourcingPolicy? policy)
    {
        if (policy == BackgroundSourcingPolicy.ClientAesthetic)
        {
            return new ClientAestheticStyleStrategy(formatKey);
        }

        return null;
    }
}

/// <summary>ClientAesthetic strategy: emits ClientAesthetic for all slides (CSS path via processor).</summary>
file sealed class ClientAestheticStyleStrategy(string formatKey) : IBackgroundSourcingStrategy
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

file sealed class ParityRasterCompositionService(string designedFormat) : IVisualCompositionService
{
    private static readonly CompositionPalette DefaultPalette = new("#333", "#666", "#999", "#FFF", "#EEE");
    private static readonly CompositionTypography DefaultTypo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions DefaultDim = new(1080, 1080, 96);

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, ClientAestheticProfile? aestheticProfile = null) =>
        Task.FromResult(BuildSpec(formatKey));

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, CarouselBackgroundPlan? plan = null, ClientAestheticProfile? aestheticProfile = null)
    {
        // Return one spec per slide by reading the payload's slide count
        int count = 1;
        try
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("slides", out var slides)
                && slides.ValueKind == JsonValueKind.Array)
            {
                count = slides.GetArrayLength();
            }
        }
        catch { /* best-effort */ }

        var specs = Enumerable.Range(1, count).Select(_ => BuildSpec(formatKey)).ToList();
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

file sealed class ParityRenderer : IContentRendererWithGeometry
{
    public bool WasCalled { get; private set; }

    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        WasCalled = true;
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake-png");
        return Task.FromResult(Result.Success(RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes)));
    }

    public Task<Result<RenderedAssetWithGeometry>> RenderWithGeometryAsync(
        VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        WasCalled = true;
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake-png");
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
        return Task.FromResult(Result.Success(new RenderedAssetWithGeometry(asset, [], null)));
    }
}

file sealed class ParityJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class ParityAnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
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

file sealed class ParityFormatResultRepository : IContentGenerationFormatResultRepository
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

file sealed class ParityOrchestrator(string formatKey, object payload, BrandSnapshot? brand = null)
    : IContentGenerationOrchestrator
{
    public Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken ct)
    {
        var results = new Dictionary<string, FormatGenerationResult<object>>(StringComparer.OrdinalIgnoreCase)
        {
            [formatKey] = FormatGenerationResult<object>.Succeeded(payload, "test.v1", [])
        };
        return Task.FromResult(new OrchestratorResult(
            OrchestratorStatus.Succeeded,
            results,
            brand ?? BrandSnapshot.FromBrandData(null, null, null)));
    }
}

file sealed class ParityPackageBuilder : IGeneratedPackageBuilder
{
    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-zip"));
        var manifest = new GeneratedPackageManifest(new Dictionary<string, FormatAssetMetadata>(), [], [], 0, "test");
        return Task.FromResult(new BuiltPackage(stream, stream.Length, manifest, "test.zip"));
    }
}

file sealed class ParityPackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class ParityPackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) =>
        Task.FromResult<GeneratedPackage?>(null);
}
