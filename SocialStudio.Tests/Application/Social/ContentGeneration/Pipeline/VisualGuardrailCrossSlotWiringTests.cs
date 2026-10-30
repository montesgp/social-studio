using System.Text.Json;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Orchestration;
using SocialStudio.Application.Features.Social.ContentGeneration.Pipeline;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Layout;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// WU-3 TDD tests — cross-slot guardrail wiring on the template-backed (enforced-background) path.
/// Spec: CLIF-CG-02. Design: D-B (non-blocking, post-priority-resolution, declared-box-only check).
///
/// <c>_guardrails.Validate</c> at ~L957 only ever validated AI-designer markup. This closes the gap
/// for the enforced-background path's actual final markup — the SECOND, separate, non-blocking call
/// added immediately after the <c>validatedMarkup</c> if/else chain closes and before the spec is
/// promoted to Html. It parses DECLARED slot boxes (no Chromium render has happened yet at this
/// point) and runs <see cref="SlotOverlapDetector"/> over them — a hit can only ever prove a builder
/// regression, since the shipped literals are hand-tuned not to overlap (WU-4 supplies real measured
/// evidence later).
/// </summary>
public sealed class VisualGuardrailCrossSlotWiringTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaa1-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbb1-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AnalysisId = Guid.Parse("ccccccc1-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid JobId = Guid.Parse("ddddddd1-dddd-dddd-dddd-dddddddddddd");

    // -----------------------------------------------------------------------
    // Unit: ExtractDeclaredSlotBoxes (internal — InternalsVisibleTo) contract
    // -----------------------------------------------------------------------

    [Fact]
    public void ExtractDeclaredSlotBoxes_ParsesLeftTopWidthMinHeight_PerDataSlot()
    {
        const string markup =
            """
            <div class="composition">
              <div data-layer="content">
                <h1 class="slot" data-slot="title" data-max-lines="3" style="position:absolute;left:104px;top:235px;width:872px;min-height:230px;color:#fff;">Title</h1>
                <p class="slot" data-slot="subtitle" data-max-lines="6" style="position:absolute;left:108px;top:540px;width:815px;min-height:310px;color:#fff;">Subtitle</p>
              </div>
            </div>
            """;

        var boxes = ContentGenerationJobProcessor.ExtractDeclaredSlotBoxes(markup);

        Assert.Equal(2, boxes.Count);
        var title = boxes.Single(b => b.Slot == "title");
        Assert.Equal(104, title.Left);
        Assert.Equal(235, title.Top);
        Assert.Equal(872, title.Width);
        Assert.Equal(230, title.Height);
    }

    [Fact]
    public void ExtractDeclaredSlotBoxes_SkipsBottomAnchoredSlots()
    {
        const string markup =
            """
            <div class="composition">
              <div data-layer="content">
                <h1 class="slot" data-slot="headline" data-max-lines="3" style="position:absolute;left:96px;top:255px;width:888px;min-height:260px;color:#fff;">Headline</h1>
                <p class="slot" data-slot="cta" data-max-lines="1" style="position:absolute;left:100px;bottom:245px;width:640px;min-height:58px;color:#fff;">CTA</p>
              </div>
            </div>
            """;

        var boxes = ContentGenerationJobProcessor.ExtractDeclaredSlotBoxes(markup);

        Assert.Single(boxes);
        Assert.Equal("headline", boxes[0].Slot);
    }

    [Fact]
    public void ExtractDeclaredSlotBoxes_RealStoryTemplateLiterals_DoNotOverlap()
    {
        // The real BuildStoryTemplateSlots literals: headline top:255 min-height:260 (bottom 515),
        // subtitle top:575 — hand-tuned to not overlap. Declared-box parsing must reflect that.
        const string markup =
            """
            <div class="composition">
              <div data-layer="content">
                <h1 class="slot" data-slot="headline" data-max-lines="3" style="position:absolute;left:96px;top:255px;width:888px;min-height:260px;color:#fff;">Headline</h1>
                <p class="slot" data-slot="subtitle" data-max-lines="6" style="position:absolute;left:100px;top:575px;width:825px;min-height:122px;color:#fff;">Subtitle</p>
              </div>
            </div>
            """;

        var boxes = ContentGenerationJobProcessor.ExtractDeclaredSlotBoxes(markup);
        var overlaps = SlotOverlapDetector.Detect(boxes);

        Assert.Empty(overlaps);
    }

    [Fact]
    public void ExtractDeclaredSlotBoxes_ThenDetect_FindsOverlapForOverlappingDeclaredBoxes()
    {
        // A hypothetical builder regression: subtitle's declared top intrudes into headline's box.
        const string markup =
            """
            <div class="composition">
              <div data-layer="content">
                <h1 class="slot" data-slot="headline" data-max-lines="3" style="position:absolute;left:96px;top:255px;width:888px;min-height:260px;color:#fff;">Headline</h1>
                <p class="slot" data-slot="subtitle" data-max-lines="6" style="position:absolute;left:100px;top:300px;width:825px;min-height:122px;color:#fff;">Subtitle</p>
              </div>
            </div>
            """;

        var boxes = ContentGenerationJobProcessor.ExtractDeclaredSlotBoxes(markup);
        var overlaps = SlotOverlapDetector.Detect(boxes);

        Assert.Single(overlaps);
        Assert.Equal("headline", overlaps[0].UpperSlot);
        Assert.Equal("subtitle", overlaps[0].LowerSlot);
    }

    // -----------------------------------------------------------------------
    // Integration: the processor wiring itself
    // -----------------------------------------------------------------------

    /// <summary>
    /// CLIF-CG-02-a/b: on the real enforced-background (template-backed) story path, the shipped
    /// literals do not overlap, so no cross-slot diagnostic is logged, the pipeline completes, and
    /// the rendered markup is the enforced-background template — proving the new check runs without
    /// false-positiving or altering markup selection.
    /// </summary>
    [Fact]
    public async Task Pipeline_EnforcedBackgroundStory_NoOverlapWarning_RendererStillCalledWithTemplateMarkup()
    {
        var rendererSpy = new CapturingGeometryRenderer();
        var capturingLogger = new CapturingLogger<ContentGenerationJobProcessor>();

        var processor = BuildProcessor(
            formatKey: "story",
            payload: new object(),
            renderer: rendererSpy,
            designer: new SucceedingDesigner(),
            guardrails: new PassingGuardrailValidator(),
            logger: capturingLogger,
            brandWithLegacyBackground: true);

        var exception = await Record.ExceptionAsync(
            () => processor.ProcessAsync(JobId, CancellationToken.None));

        Assert.Null(exception);
        Assert.True(rendererSpy.WasCalled);
        Assert.NotNull(rendererSpy.ReceivedMarkup);
        Assert.Contains("https://cdn.example.com/manual-bg.png", rendererSpy.ReceivedMarkup);

        var overlapWarnings = capturingLogger.Warnings
            .Where(w => w.Contains("cross-slot overlap", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(overlapWarnings);
    }

    /// <summary>
    /// Gating: when there is no enforced background (pure AI-markup path), the check must not fire
    /// either way — verified indirectly (no overlap warning, since the check is skipped entirely)
    /// while confirming the AI markup — not a template — is what actually rendered.
    /// </summary>
    [Fact]
    public async Task Pipeline_AiMarkupPath_NoEnforcedBackground_NoOverlapWarning()
    {
        var rendererSpy = new CapturingGeometryRenderer();
        var capturingLogger = new CapturingLogger<ContentGenerationJobProcessor>();

        var processor = BuildProcessor(
            formatKey: "story",
            payload: new object(),
            renderer: rendererSpy,
            designer: new SucceedingDesigner(),
            guardrails: new PassingGuardrailValidator(),
            logger: capturingLogger,
            brandWithLegacyBackground: false);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        Assert.True(rendererSpy.WasCalled);
        Assert.NotNull(rendererSpy.ReceivedMarkup);
        Assert.Contains("class=\"layer-background\"", rendererSpy.ReceivedMarkup);

        var overlapWarnings = capturingLogger.Warnings
            .Where(w => w.Contains("cross-slot overlap", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(overlapWarnings);
    }

    // -----------------------------------------------------------------------
    // Helpers (mirrors ContentGenerationJobProcessorVisualTests' fixture)
    // -----------------------------------------------------------------------

    private static ContentGenerationJobProcessor BuildProcessor(
        string formatKey,
        object payload,
        IContentRenderer renderer,
        IVisualDesigner designer,
        IVisualGuardrailValidator guardrails,
        ILogger<ContentGenerationJobProcessor> logger,
        bool brandWithLegacyBackground = false)
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
            DetectedHooks: [],
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

        var brand = brandWithLegacyBackground
            ? BuildBrandWithLegacyBackground()
            : BrandSnapshot.FromBrandData(null, null, null);

        var brandLoader = brandWithLegacyBackground
            ? (IClientBrandSnapshotLoader)new VgcsLegacyBackgroundBrandLoader()
            : new VgcsEmptyBrandLoader();

        return new ContentGenerationJobProcessor(
            jobs: new VgcsStubJobRepository(fakeJob),
            analyses: new VgcsStubAnalysisRepository(analysisRecord),
            orchestrator: new VgcsStubOrchestrator(formatKey, payload, brand),
            formatResults: new VgcsStubFormatResultRepository(),
            brandLoader: brandLoader,
            composer: new VgcsRasterCompositionService(formatKey),
            renderer: renderer,
            packageBuilder: new VgcsStubPackageBuilder(),
            packageStorage: new VgcsStubPackageStorage(),
            packages: new VgcsStubPackageRepository(),
            assetStorage: null,
            assetRepository: null,
            designer: designer,
            guardrails: guardrails,
            logger: logger,
            backgroundStrategyResolver: null);
    }

    private static BrandSnapshot BuildBrandWithLegacyBackground()
    {
        var brand = new BrandManualSnapshot(
            ColorPalette: new[] { new WeightedColor("#FFFFFF", null, 0), new WeightedColor("#000000", null, 1) },
            Logos: Array.Empty<string>(),
            BackgroundTemplates:
            [
                new BrandBackgroundTemplateSnapshot(
                    "manual-bg",
                    "https://cdn.example.com/manual-bg.png",
                    null,
                    BackgroundTier.Creative,
                    null,
                    false)
            ],
            Typography: new[] { "Inter" },
            VoiceAndTone: null,
            Notes: null);
        return BrandSnapshot.FromBrandData(brand, null, null);
    }
}

// ─── Capturing logger ─────────────────────────────────────────────────────────

file sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _warnings = new();

    public IReadOnlyList<string> Warnings => _warnings;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
        {
            _warnings.Add(formatter(state, exception));
        }
    }
}

// ─── Test doubles (Vgcs prefix to avoid collisions with other test files) ────

file sealed class CapturingGeometryRenderer : IContentRendererWithGeometry
{
    public bool WasCalled { get; private set; }
    public string? ReceivedMarkup { get; private set; }

    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        WasCalled = true;
        ReceivedMarkup = spec.DesignerMarkup;
        var bytes = System.Text.Encoding.UTF8.GetBytes($"fake-{spec.FormatKey}-content");
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
        return Task.FromResult(Result<RenderedAsset>.Success(asset));
    }

    public Task<Result<RenderedAssetWithGeometry>> RenderWithGeometryAsync(
        VisualCompositionSpec spec,
        RenderOptions opts,
        CancellationToken ct)
    {
        WasCalled = true;
        ReceivedMarkup = spec.DesignerMarkup;
        var bytes = System.Text.Encoding.UTF8.GetBytes($"fake-{spec.FormatKey}-content");
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
        return Task.FromResult(Result.Success(new RenderedAssetWithGeometry(asset, [], null)));
    }
}

file sealed class SucceedingDesigner : IVisualDesigner
{
    public bool IsActive => true;

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken,
        SocialStudio.Application.Features.Social.ContentGeneration.Shared.ClientAestheticDesignContext? aesthetic = null)
    {
        var output = new VisualDesignOutput(
            Markup: """<div class="composition" data-format="story" style="width:1080px;height:1920px;"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;">Test</h1></div></div>""",
            PromptVersionKey: "visual-designer/v1",
            IsValid: false);
        return Task.FromResult(Result.Success(output));
    }
}

file sealed class PassingGuardrailValidator : IVisualGuardrailValidator
{
    public Result<VisualDesignOutput> Validate(VisualDesignOutput output, BrandManualSnapshot brand, VisualCompositionSpec spec) =>
        Result.Success(output.WithValidated());
}

file sealed class VgcsEmptyBrandLoader : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(BrandSnapshot.FromBrandData(null, null, null));
}

file sealed class VgcsLegacyBackgroundBrandLoader : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default)
    {
        var brand = new BrandManualSnapshot(
            ColorPalette: new[] { new WeightedColor("#FFFFFF", null, 0), new WeightedColor("#000000", null, 1) },
            Logos: Array.Empty<string>(),
            BackgroundTemplates:
            [
                new BrandBackgroundTemplateSnapshot(
                    "manual-bg",
                    "https://cdn.example.com/manual-bg.png",
                    null,
                    BackgroundTier.Creative,
                    null,
                    false)
            ],
            Typography: new[] { "Inter" },
            VoiceAndTone: null,
            Notes: null);
        return Task.FromResult(BrandSnapshot.FromBrandData(brand, null, null));
    }
}

/// <summary>Composition service that returns a Raster spec for the designed format under test.</summary>
file sealed class VgcsRasterCompositionService(string designedFormat) : IVisualCompositionService
{
    private static readonly CompositionPalette DefaultPalette = new("#333", "#666", "#999", "#FFF", "#EEE");
    private static readonly CompositionTypography DefaultTypo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions DefaultDim = new(1080, 1920, 96);

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, ClientAestheticProfile? aestheticProfile = null) =>
        Task.FromResult(BuildSpec(formatKey));

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, CarouselBackgroundPlan? plan = null, ClientAestheticProfile? aestheticProfile = null) =>
        Task.FromResult<IReadOnlyList<VisualCompositionSpec>>([BuildSpec(formatKey)]);

    private VisualCompositionSpec BuildSpec(string formatKey)
    {
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

file sealed class VgcsStubJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class VgcsStubAnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
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

file sealed class VgcsStubFormatResultRepository : IContentGenerationFormatResultRepository
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

file sealed class VgcsStubOrchestrator(string formatKey, object payload, BrandSnapshot? brand = null) : IContentGenerationOrchestrator
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

file sealed class VgcsStubPackageBuilder : IGeneratedPackageBuilder
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

file sealed class VgcsStubPackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake-storage://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class VgcsStubPackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) => Task.FromResult<GeneratedPackage?>(null);
}
