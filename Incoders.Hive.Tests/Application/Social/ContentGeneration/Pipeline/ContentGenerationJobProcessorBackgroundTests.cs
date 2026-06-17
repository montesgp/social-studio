using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// WU-9 TDD tests — background image capture in the export envelope.
///
/// Spec: When a geometry-aware renderer returns a <c>BackgroundImageDataUri</c>, the processor
/// MUST persist it as <c>CompositionSpecEnvelope.BackgroundImageRef</c> inside
/// <c>composition_spec_json</c>. This makes the background PNG available to the PPTX/Canva
/// export adapters without re-rendering.
///
/// Tests run without Chromium — the renderer is a pure in-process stub.
/// </summary>
public sealed class ContentGenerationJobProcessorBackgroundTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid JobId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private const string FakeDataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUg==";

    // -----------------------------------------------------------------------
    // WU-9 RED: when geometry renderer returns BackgroundImageDataUri, it must
    // be persisted as BackgroundImageRef in composition_spec_json.
    // -----------------------------------------------------------------------

    /// <summary>
    /// GIVEN a renderer that implements IContentRendererWithGeometry and returns a non-null BackgroundImageDataUri,
    /// WHEN the processor runs a designed-Html format,
    /// THEN the persisted CompositionSpecEnvelope.BackgroundImageRef equals the captured data-URI.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_GeometryRendererWithBackground_PersistsBackgroundRefInEnvelope()
    {
        var formatResultRepo = new CapturingFormatResultRepository();
        var renderer = new StubGeometryRendererWithBackground(FakeDataUri);

        var processor = BuildProcessor(
            formatKey: "story",
            payload: new object(),
            renderer: renderer,
            formatResults: formatResultRepo);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        // Assert: a Rendered result was persisted
        var persisted = formatResultRepo.LastUpserted;
        Assert.NotNull(persisted);
        Assert.Equal("Rendered", persisted!.Status);

        // Assert: composition_spec_json was written with the background ref
        Assert.NotNull(persisted.CompositionSpecJson);

        var envelope = JsonSerializer.Deserialize<CompositionSpecEnvelope>(
            persisted.CompositionSpecJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(envelope);
        Assert.Equal(FakeDataUri, envelope!.BackgroundImageRef);
    }

    /// <summary>
    /// GIVEN a renderer that returns a null BackgroundImageDataUri (background capture failed gracefully),
    /// WHEN the processor runs,
    /// THEN BackgroundImageRef is null in the persisted envelope (no crash, export degrades gracefully).
    /// </summary>
    [Fact]
    public async Task ProcessAsync_GeometryRendererWithNullBackground_PersistsNullBackgroundRef()
    {
        var formatResultRepo = new CapturingFormatResultRepository();
        var renderer = new StubGeometryRendererWithBackground(backgroundDataUri: null);

        var processor = BuildProcessor(
            formatKey: "story",
            payload: new object(),
            renderer: renderer,
            formatResults: formatResultRepo);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        var persisted = formatResultRepo.LastUpserted;
        Assert.NotNull(persisted);
        Assert.Equal("Rendered", persisted!.Status);
        Assert.NotNull(persisted.CompositionSpecJson);

        var envelope = JsonSerializer.Deserialize<CompositionSpecEnvelope>(
            persisted.CompositionSpecJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(envelope);
        Assert.Null(envelope!.BackgroundImageRef);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private ContentGenerationJobProcessor BuildProcessor(
        string formatKey,
        object payload,
        IContentRenderer renderer,
        IContentGenerationFormatResultRepository formatResults)
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

        // Active designer that returns Html markup (triggering the geometry-aware render path)
        var designer = new HtmlOutputDesigner();
        var guardrails = new AlwaysPassGuardrails();
        var brand = BrandSnapshot.FromBrandData(
            new BrandManualSnapshot(
                ColorPalette: ["#FFFFFF"],
                Logos: [],
                BackgroundTemplates: [],
                Typography: ["Inter"],
                VoiceAndTone: null,
                Notes: null),
            null, null);

        return new ContentGenerationJobProcessor(
            jobs: new StubJobRepository(fakeJob),
            analyses: new StubAnalysisRepository(analysisRecord),
            orchestrator: new StubOrchestrator(formatKey, payload, brand),
            formatResults: formatResults,
            brandLoader: new StaticBrandLoader(brand),
            composer: new HtmlCompositionService(formatKey),
            renderer: renderer,
            packageBuilder: new StubPackageBuilder(),
            packageStorage: new StubPackageStorage(),
            packages: new StubPackageRepository(),
            assetStorage: null,
            assetRepository: null,
            designer: designer,
            guardrails: guardrails,
            logger: NullLogger<ContentGenerationJobProcessor>.Instance);
    }
}

// -----------------------------------------------------------------------
// WU-9 stubs — local to this file
// -----------------------------------------------------------------------

/// <summary>
/// Spy: captures the last upserted format result so the test can assert on composition_spec_json.
/// </summary>
file sealed class CapturingFormatResultRepository : IContentGenerationFormatResultRepository
{
    private readonly Dictionary<(Guid, string), ContentGenerationFormatResult> _store = new();

    public ContentGenerationFormatResult? LastUpserted { get; private set; }

    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult r, CancellationToken ct)
    {
        _store[(r.JobId, r.FormatKey)] = r;
        // Only capture Rendered results (not intermediate AiSucceeded writes)
        if (r.Status == "Rendered")
        {
            LastUpserted = r;
        }
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>([]);
}

/// <summary>
/// Geometry-aware renderer stub. Returns a fake PNG asset and the given background data-URI.
/// Implements IContentRendererWithGeometry so the processor takes the geometry-aware code path.
/// </summary>
file sealed class StubGeometryRendererWithBackground(string? backgroundDataUri) : IContentRendererWithGeometry
{
    private static readonly byte[] FakePngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", FakePngBytes);
        return Task.FromResult(Result.Success(asset));
    }

    public Task<Result<RenderedAssetWithGeometry>> RenderWithGeometryAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        CancellationToken ct)
    {
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", FakePngBytes);
        // WU-9: BackgroundImageDataUri is the field we add in this work unit
        var result = new RenderedAssetWithGeometry(asset, [], backgroundDataUri);
        return Task.FromResult(Result.Success(result));
    }
}

/// <summary>Active designer that returns valid Html markup — triggers the Html render path.</summary>
file sealed class HtmlOutputDesigner : IVisualDesigner
{
    public bool IsActive => true;

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken)
    {
        const string markup = """<div class="composition" data-format="story" style="width:1080px;height:1920px;"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"><h1 data-slot="headline" style="font-family:Inter;color:#FFFFFF;">Test</h1></div></div>""";
        return Task.FromResult(Result.Success(new VisualDesignOutput(
            Markup: markup,
            PromptVersionKey: "visual-designer/v1",
            IsValid: false)));
    }
}

/// <summary>Guardrails that always pass through.</summary>
file sealed class AlwaysPassGuardrails : IVisualGuardrailValidator
{
    public Result<VisualDesignOutput> Validate(VisualDesignOutput output, BrandManualSnapshot brand, VisualCompositionSpec spec) =>
        Result.Success(output.WithValidated());
}

/// <summary>Composition service that returns an Html spec for designed formats (story/carousel).</summary>
file sealed class HtmlCompositionService(string designedFormat) : IVisualCompositionService
{
    private static readonly CompositionPalette DefaultPalette = new("#333", "#666", "#999", "#FFF", "#EEE");
    private static readonly CompositionTypography DefaultTypo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions DefaultDim = new(1080, 1920, 96);

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct)
    {
        return Task.FromResult(BuildSpec(formatKey));
    }

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<VisualCompositionSpec>>([BuildSpec(formatKey)]);
    }

    private VisualCompositionSpec BuildSpec(string formatKey)
    {
        // Designed formats return Raster kind — the designer+guardrail stage will then promote to Html.
        var outputKind = string.Equals(formatKey, designedFormat, StringComparison.OrdinalIgnoreCase)
            ? CompositionOutputKind.Raster
            : CompositionOutputKind.Text;

        return new VisualCompositionSpec(
            FormatKey: formatKey,
            LayoutType: "story-story",
            Palette: DefaultPalette,
            Typography: DefaultTypo,
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = "Test" },
            Dimensions: DefaultDim)
        with { OutputKind = outputKind, TextBody = outputKind == CompositionOutputKind.Text ? "fake body" : null };
    }

    public IReadOnlyList<string> GetInferredFields() => [];
}

file sealed class StubJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class StubAnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
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

file sealed class StubOrchestrator(string formatKey, object payload, BrandSnapshot? brand = null) : IContentGenerationOrchestrator
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

file sealed class StaticBrandLoader(BrandSnapshot brand) : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(brand);
}

file sealed class StubPackageBuilder : IGeneratedPackageBuilder
{
    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-zip"));
        var manifest = new GeneratedPackageManifest(
            new Dictionary<string, FormatAssetMetadata>(),
            [], [], 0,
            input.ClientName ?? "test-client");
        return Task.FromResult(new BuiltPackage(stream, stream.Length, manifest, "test-package.zip"));
    }
}

file sealed class StubPackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake-storage://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class StubPackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) => Task.FromResult<GeneratedPackage?>(null);
}
