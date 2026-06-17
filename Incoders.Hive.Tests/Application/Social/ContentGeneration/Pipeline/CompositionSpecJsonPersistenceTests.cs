using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// B3 WU-1 TDD tests — composition_spec_json persistence.
///
/// PPTX-01: a successful render MUST persist a non-null, well-formed CompositionSpecEnvelope
/// in <c>composition_spec_json</c>.
/// PPTX-04: the same persisted markup drives both the PPTX and Canva export paths.
/// D1 design decision: no EF migration — reuses the existing column.
/// D3 design decision: geometry is threaded from the renderer (via IContentRendererWithGeometry)
/// into the envelope; absent geometry results in an empty SlotGeometry list, not a null.
///
/// RED: these tests fail until the write-site fix + IContentRendererWithGeometry wiring
/// are in place.
/// </summary>
public sealed class CompositionSpecJsonPersistenceTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid JobId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------------
    // PPTX-01-a: successful render → non-null CompositionSpecJson
    // -----------------------------------------------------------------------

    /// <summary>
    /// PPTX-01 Scenario a: after a successful designer+render pass, the persisted format result
    /// MUST have a non-null <c>CompositionSpecJson</c> containing the validated markup.
    /// </summary>
    [Fact]
    public async Task Pipeline_SuccessfulDesignerRender_PersistsNonNullCompositionSpecJson()
    {
        // GIVEN a processor wired with an active designer + passing guardrails +
        //       a renderer that returns a valid asset
        const string expectedMarkup = "<html data-slot='headline'>Test</html>";
        var formatResults = new SpyFormatResultRepository();
        var processor = BuildProcessor(
            formatKey: "story",
            markup: expectedMarkup,
            formatResults: formatResults);

        // WHEN processing runs
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN the rendered format result MUST have a non-null CompositionSpecJson
        var rendered = formatResults.Upserts.FirstOrDefault(r => r.Status == "Rendered");
        Assert.NotNull(rendered);
        Assert.NotNull(rendered!.CompositionSpecJson);
        Assert.False(string.IsNullOrWhiteSpace(rendered.CompositionSpecJson),
            "CompositionSpecJson must not be null or empty after a successful render.");
    }

    /// <summary>
    /// The persisted JSON MUST deserialize to a valid <see cref="CompositionSpecEnvelope"/>
    /// with <c>SchemaVersion = 1</c> and the expected markup.
    /// </summary>
    [Fact]
    public async Task Pipeline_SuccessfulDesignerRender_EnvelopeHasCorrectSchemaVersionAndMarkup()
    {
        // GIVEN
        const string expectedMarkup = "<div data-slot='headline'>Hello</div>";
        var formatResults = new SpyFormatResultRepository();
        var processor = BuildProcessor("story", expectedMarkup, formatResults);

        // WHEN
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN the JSON round-trips into a valid envelope
        var rendered = formatResults.Upserts.First(r => r.Status == "Rendered");
        var envelope = JsonSerializer.Deserialize<CompositionSpecEnvelope>(
            rendered.CompositionSpecJson!, JsonOpts);

        Assert.NotNull(envelope);
        Assert.Equal(CompositionSpecEnvelope.CurrentSchemaVersion, envelope!.SchemaVersion);
        Assert.Equal(expectedMarkup, envelope.Markup);
    }

    /// <summary>
    /// The persisted envelope MUST include canvas dimensions matching the spec.
    /// </summary>
    [Fact]
    public async Task Pipeline_SuccessfulDesignerRender_EnvelopeContainsCanvasDimensions()
    {
        // GIVEN a spec with known dimensions
        const string markup = "<div data-slot='headline'>Canvas test</div>";
        var formatResults = new SpyFormatResultRepository();
        var processor = BuildProcessor(
            "story", markup, formatResults,
            specWidth: 1080, specHeight: 1920, specDpi: 96);

        // WHEN
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN canvas is stored in the envelope
        var rendered = formatResults.Upserts.First(r => r.Status == "Rendered");
        var envelope = JsonSerializer.Deserialize<CompositionSpecEnvelope>(
            rendered.CompositionSpecJson!, JsonOpts);

        Assert.NotNull(envelope!.Canvas);
        Assert.Equal(1080, envelope.Canvas.Width);
        Assert.Equal(1920, envelope.Canvas.Height);
        Assert.Equal(96, envelope.Canvas.Dpi);
    }

    // -----------------------------------------------------------------------
    // PPTX-01-a (D3): geometry captured via IContentRendererWithGeometry
    // -----------------------------------------------------------------------

    /// <summary>
    /// When the renderer implements <see cref="IContentRendererWithGeometry"/>, the
    /// captured geometry array MUST be persisted in the envelope (non-empty).
    /// </summary>
    [Fact]
    public async Task Pipeline_RendererWithGeometry_SlotGeometryPersistedInEnvelope()
    {
        // GIVEN a renderer that also returns geometry
        const string markup = "<div data-slot='headline'>Geom test</div>";
        var geometry = new List<SlotGeometryEntry>
        {
            new("headline", X: 10, Y: 20, Width: 500, Height: 80,
                FontFamily: "Inter", FontSizePx: 48, Color: "#FFFFFF", TextAlign: "left")
        };
        var formatResults = new SpyFormatResultRepository();
        var processor = BuildProcessorWithGeometryRenderer(
            "story", markup, geometry, formatResults);

        // WHEN
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN geometry appears in the persisted envelope
        var rendered = formatResults.Upserts.First(r => r.Status == "Rendered");
        var envelope = JsonSerializer.Deserialize<CompositionSpecEnvelope>(
            rendered.CompositionSpecJson!, JsonOpts);

        Assert.NotNull(envelope);
        Assert.Single(envelope!.SlotGeometry);
        var geo = envelope.SlotGeometry[0];
        Assert.Equal("headline", geo.Slot);
        Assert.Equal(10, geo.X);
        Assert.Equal(20, geo.Y);
        Assert.Equal(500, geo.Width);
        Assert.Equal(80, geo.Height);
        Assert.Equal("Inter", geo.FontFamily);
        Assert.Equal(48, geo.FontSizePx);
    }

    /// <summary>
    /// When the renderer does NOT implement IContentRendererWithGeometry (legacy/fallback renderer),
    /// the envelope MUST still be persisted with an empty SlotGeometry list (not null, not failed).
    /// </summary>
    [Fact]
    public async Task Pipeline_LegacyRendererWithoutGeometry_EnvelopePersistedWithEmptySlotGeometry()
    {
        // GIVEN a legacy renderer (IContentRenderer only, no geometry)
        const string markup = "<div data-slot='headline'>No geom</div>";
        var formatResults = new SpyFormatResultRepository();
        var processor = BuildProcessor("story", markup, formatResults);

        // WHEN
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN envelope is still persisted, SlotGeometry is empty (not null)
        var rendered = formatResults.Upserts.First(r => r.Status == "Rendered");
        var envelope = JsonSerializer.Deserialize<CompositionSpecEnvelope>(
            rendered.CompositionSpecJson!, JsonOpts);

        Assert.NotNull(envelope);
        Assert.NotNull(envelope!.SlotGeometry);
        Assert.Empty(envelope.SlotGeometry);
    }

    // -----------------------------------------------------------------------
    // Resilience: guardrail rejection falls back to a deterministic composition
    // -----------------------------------------------------------------------

    /// <summary>
    /// When guardrail validation rejects the AI design, the pipeline MUST fall back to a
    /// deterministic brand-bound composition and persist THAT markup — the flow completes for any
    /// model. The rejected AI markup MUST NOT be stored; the deterministic fallback MUST be.
    /// </summary>
    [Fact]
    public async Task Pipeline_GuardrailFail_PersistsDeterministicFallbackMarkup()
    {
        // GIVEN guardrails always fail (the AI markup is rejected)
        var formatResults = new SpyFormatResultRepository();
        var processor = BuildProcessorWithFailingGuardrails(
            "story", "<div>Should fail</div>", formatResults);

        // WHEN
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN a "Rendered" row IS persisted with the deterministic fallback markup — NOT the
        // rejected AI markup.
        var rendered = formatResults.Upserts
            .Where(r => r.Status == "Rendered" && r.CompositionSpecJson is not null)
            .ToList();

        Assert.Single(rendered);
        // The deterministic fallback synthesizes a brand-palette gradient background; its presence
        // (and the absence of the rejected AI markup) proves the fallback was used.
        Assert.Contains("linear-gradient", rendered[0].CompositionSpecJson!);
        Assert.DoesNotContain("Should fail", rendered[0].CompositionSpecJson!);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private ContentGenerationJobProcessor BuildProcessor(
        string formatKey,
        string markup,
        IContentGenerationFormatResultRepository formatResults,
        int specWidth = 1080,
        int specHeight = 1920,
        int specDpi = 96)
    {
        return BuildProcessorCore(
            formatKey,
            markup,
            renderer: new StubRendererWithoutGeometry(),
            formatResults: formatResults,
            guardrails: new AlwaysPassingGuardrails(),
            specWidth: specWidth,
            specHeight: specHeight,
            specDpi: specDpi);
    }

    private ContentGenerationJobProcessor BuildProcessorWithGeometryRenderer(
        string formatKey,
        string markup,
        IReadOnlyList<SlotGeometryEntry> geometry,
        IContentGenerationFormatResultRepository formatResults)
    {
        return BuildProcessorCore(
            formatKey,
            markup,
            renderer: new StubRendererWithGeometry(geometry),
            formatResults: formatResults,
            guardrails: new AlwaysPassingGuardrails());
    }

    private ContentGenerationJobProcessor BuildProcessorWithFailingGuardrails(
        string formatKey,
        string markup,
        IContentGenerationFormatResultRepository formatResults)
    {
        return BuildProcessorCore(
            formatKey,
            markup,
            renderer: new StubRendererWithoutGeometry(),
            formatResults: formatResults,
            guardrails: new AlwaysFailingGuardrails());
    }

    private ContentGenerationJobProcessor BuildProcessorCore(
        string formatKey,
        string markup,
        IContentRenderer renderer,
        IContentGenerationFormatResultRepository formatResults,
        IVisualGuardrailValidator guardrails,
        int specWidth = 1080,
        int specHeight = 1920,
        int specDpi = 96)
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

        var analysisJson = JsonSerializer.Serialize(analysis, JsonOpts);
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

        var designer = new StubMarkupDesigner(markup);
        var composer = new HtmlCompositionService(specWidth, specHeight, specDpi);

        return new ContentGenerationJobProcessor(
            jobs: new StubJobRepository(fakeJob),
            analyses: new StubAnalysisRepository(analysisRecord),
            orchestrator: new StubOrchestrator(formatKey),
            formatResults: formatResults,
            brandLoader: new StubBrandLoader(),
            composer: composer,
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
// Spy / stub implementations (file-scoped)
// -----------------------------------------------------------------------

/// <summary>Captures all UpsertAsync calls for assertion.</summary>
file sealed class SpyFormatResultRepository : IContentGenerationFormatResultRepository
{
    private readonly Dictionary<(Guid JobId, string FormatKey), ContentGenerationFormatResult> _store = new();
    public List<ContentGenerationFormatResult> Upserts { get; } = [];

    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult r, CancellationToken ct)
    {
        var key = (r.JobId, r.FormatKey);
        var persisted = _store.TryGetValue(key, out var existing)
            ? r with { Id = existing.Id, CreatedAt = existing.CreatedAt }
            : r;
        Upserts.Add(persisted);
        _store[key] = persisted;
        return Task.FromResult(persisted);
    }

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>(
            _store.Values.Where(r => r.JobId == jobId).ToList());
}

/// <summary>
/// Renderer that implements only <see cref="IContentRenderer"/> (no geometry support).
/// Simulates a legacy/fallback renderer.
/// </summary>
file sealed class StubRendererWithoutGeometry : IContentRenderer
{
    public Task<Result<RenderedAsset>> RenderAsync(
        VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"fake-{spec.FormatKey}-content");
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
        return Task.FromResult(Result.Success(asset));
    }
}

/// <summary>
/// Renderer that implements <see cref="IContentRendererWithGeometry"/>,
/// returning a pre-configured geometry list alongside the asset.
/// </summary>
file sealed class StubRendererWithGeometry(
    IReadOnlyList<SlotGeometryEntry> geometry) : IContentRendererWithGeometry
{
    public Task<Result<RenderedAsset>> RenderAsync(
        VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"fake-{spec.FormatKey}-content");
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
        return Task.FromResult(Result.Success(asset));
    }

    public Task<Result<RenderedAssetWithGeometry>> RenderWithGeometryAsync(
        VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"fake-{spec.FormatKey}-content");
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
        var result = new RenderedAssetWithGeometry(asset, geometry);
        return Task.FromResult(Result.Success(result));
    }
}

/// <summary>Active designer stub that returns a given markup as validated output.</summary>
file sealed class StubMarkupDesigner(string markup) : IVisualDesigner
{
    public bool IsActive => true;

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken)
    {
        var output = new VisualDesignOutput(
            Markup: markup,
            PromptVersionKey: "stub/v1",
            IsValid: false);
        return Task.FromResult(Result.Success(output));
    }
}

/// <summary>Guardrail validator that always passes, returning the markup unchanged.</summary>
file sealed class AlwaysPassingGuardrails : IVisualGuardrailValidator
{
    public Result<VisualDesignOutput> Validate(
        VisualDesignOutput design,
        BrandManualSnapshot brand,
        VisualCompositionSpec spec)
    {
        return Result.Success(design with { IsValid = true });
    }
}

/// <summary>Guardrail validator that always fails.</summary>
file sealed class AlwaysFailingGuardrails : IVisualGuardrailValidator
{
    public Result<VisualDesignOutput> Validate(
        VisualDesignOutput design,
        BrandManualSnapshot brand,
        VisualCompositionSpec spec)
    {
        return Result.Failure<VisualDesignOutput>(VisualGuardrailErrors.InvalidMarkup);
    }
}

/// <summary>
/// Composition service that produces an Html spec with DesignerMarkup pre-set
/// to a given markup string (simulates the spec just before the designer+guardrail stage).
/// The designer stub overwrites DesignerMarkup during ComposeAsync, so the pre-set value
/// here is the fallback for non-designed specs.
/// </summary>
file sealed class HtmlCompositionService(
    int width,
    int height,
    int dpi) : IVisualCompositionService
{
    public Task<VisualCompositionSpec> ComposeAsync(
        string fmtKey, object payload, BrandSnapshot brand, CancellationToken ct)
    {
        var spec = BuildSpec(fmtKey);
        return Task.FromResult(spec);
    }

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string fmtKey, object payload, BrandSnapshot brand, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<VisualCompositionSpec>>([BuildSpec(fmtKey)]);
    }

    public IReadOnlyList<string> GetInferredFields() => [];

    private VisualCompositionSpec BuildSpec(string fmtKey) =>
        new VisualCompositionSpec(
            FormatKey: fmtKey,
            LayoutType: "story-story",
            Palette: new CompositionPalette("#111", "#222", "#333", "#FFF", "#EEE"),
            Typography: new CompositionTypography("Inter", 24, "Inter", 14),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = "Test" },
            Dimensions: new CompositionDimensions(width, height, dpi))
        with { OutputKind = CompositionOutputKind.Raster };  // Raster so designer stage triggers
}

file sealed class StubOrchestrator(string formatKey) : IContentGenerationOrchestrator
{
    public Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken ct)
    {
        var results = new Dictionary<string, FormatGenerationResult<object>>(StringComparer.OrdinalIgnoreCase)
        {
            [formatKey] = FormatGenerationResult<object>.Succeeded(new object(), "stub.v1", [])
        };

        return Task.FromResult(new OrchestratorResult(
            OrchestratorStatus.Succeeded,
            results,
            BrandSnapshot.FromBrandData(null, null, null)));
    }
}

file sealed class StubJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid orgId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
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

file sealed class StubBrandLoader : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(BrandSnapshot.FromBrandData(null, null, null));
}

file sealed class StubPackageBuilder : IGeneratedPackageBuilder
{
    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-zip"));
        var manifest = new GeneratedPackageManifest(
            new Dictionary<string, FormatAssetMetadata>(),
            [], [], 0, input.ClientName ?? "test");
        return Task.FromResult(new BuiltPackage(stream, stream.Length, manifest, "test.zip"));
    }
}

file sealed class StubPackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake://test"));
    public Task<Stream> OpenAsync(StoredPackageRef r, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef r, CancellationToken ct) => Task.CompletedTask;
}

file sealed class StubPackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid orgId, CancellationToken ct) => Task.FromResult<GeneratedPackage?>(null);
}
