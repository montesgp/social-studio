using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Observability;
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
/// B1a TDD pipeline tests for designer + guardrail integration.
/// Tasks: 5.1 (guardrail-fail skips renderer), 5.2 (Tier-1 forced default reaches designer).
/// Tasks: B1a-fix (no-op/inactive designer must not fail flyer/carousel — passthrough to legacy renderer).
/// Spec: BG-03 (Tier-1 returned without generation), VD-02/04 (renderer not called on failure).
/// </summary>
public sealed class ContentGenerationJobProcessorVisualTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid JobId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    // -----------------------------------------------------------------------
    // Resilience: guardrail rejection MUST NOT fail the format — fall back to a
    // deterministic brand-bound composition so the flow completes for ANY model.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Pipeline_GuardrailFail_FallsBackToDeterministicComposition()
    {
        // GIVEN a designer whose markup the guardrail rejects (e.g. a weak model's off-schema
        // output). The pipeline MUST NOT fail the format — it falls back to a deterministic
        // brand-bound composition so the flyer is still produced.
        var rendererSpy = new CapturingGeometryRenderer();
        var guardrailsFailing = new FailingGuardrailValidator();
        var designer = new SucceedingDesigner();

        var processor = BuildProcessor(
            formatKey: "flyer",
            payload: new object(),
            renderer: rendererSpy,
            designer: designer,
            guardrails: guardrailsFailing);

        // WHEN processing runs
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN the renderer IS called with deterministic fallback markup (a valid composition),
        // NOT the AI markup the guardrail rejected.
        Assert.True(rendererSpy.WasCalled,
            "Renderer MUST be called — the flyer falls back to a deterministic composition instead of failing.");
        Assert.NotNull(rendererSpy.ReceivedMarkup);
        Assert.Contains("class=\"composition\"", rendererSpy.ReceivedMarkup);
        Assert.Contains("linear-gradient", rendererSpy.ReceivedMarkup);
        // The AI stub emits class="layer-background"; the deterministic fallback does not — this
        // proves the rejected AI markup was discarded in favor of the fallback.
        Assert.DoesNotContain("class=\"layer-background\"", rendererSpy.ReceivedMarkup);
    }

    // -----------------------------------------------------------------------
    // Task 5.2 [TEST — RED] BG-03: Tier-1 forced default reaches designer with ForcedDefault arg
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Pipeline_Tier1ForcedDefault_DesignerReceivesForcedDefaultUrl()
    {
        // GIVEN snapshot with ForcedDefault entry for "flyer"
        var designerSpy = new SpyDesigner();

        var processor = BuildProcessor(
            formatKey: "flyer",
            payload: new object(),
            renderer: new SpyRenderer(),
            designer: designerSpy,
            guardrails: new PassingGuardrailValidator(),
            brandWithForcedDefault: true);

        // WHEN processing runs
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN designer's backgroundSelection arg is BackgroundSelection.ForcedDefault
        Assert.NotNull(designerSpy.ReceivedSelection);
        Assert.IsType<BackgroundSelection.ForcedDefault>(designerSpy.ReceivedSelection);
    }

    [Fact]
    public async Task Pipeline_Tier1ForcedDefault_OverridesDesignerBackgroundLayerBeforeRender()
    {
        // GIVEN the AI designer returns a generic black background, but the client brand manual
        // defines a forced default flyer template
        var rendererSpy = new CapturingGeometryRenderer();

        var processor = BuildProcessor(
            formatKey: "flyer",
            payload: new object(),
            renderer: rendererSpy,
            designer: new SucceedingDesigner(),
            guardrails: new PassingGuardrailValidator(),
            brandWithForcedDefault: true);

        // WHEN processing runs
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN the renderer receives markup whose background layer is the forced brand template,
        // not whatever free-form background the AI invented.
        Assert.True(rendererSpy.WasCalled);
        Assert.NotNull(rendererSpy.ReceivedMarkup);
        Assert.Contains("https://cdn.example.com/forced-flyer-bg.png", rendererSpy.ReceivedMarkup);
        Assert.Contains("data-background-source=\"brand-manual-forced-default\"", rendererSpy.ReceivedMarkup);
        Assert.DoesNotContain("class=\"layer-background\"", rendererSpy.ReceivedMarkup);
    }

    [Fact]
    public async Task Pipeline_AnyManualBackgroundTemplate_OverridesDesignerBackgroundLayerBeforeRender()
    {
        // GIVEN a client has a background image in the manual, but it is a legacy/default row
        // rather than a ForcedDefault row
        var rendererSpy = new CapturingGeometryRenderer();

        var processor = BuildProcessor(
            formatKey: "flyer",
            payload: new object(),
            renderer: rendererSpy,
            designer: new SucceedingDesigner(),
            guardrails: new PassingGuardrailValidator(),
            brandWithLegacyBackground: true);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        Assert.True(rendererSpy.WasCalled);
        Assert.NotNull(rendererSpy.ReceivedMarkup);
        Assert.Contains("https://cdn.example.com/manual-bg.png", rendererSpy.ReceivedMarkup);
        Assert.Contains("data-background-source=\"brand-manual-template\"", rendererSpy.ReceivedMarkup);
        Assert.DoesNotContain("class=\"layer-background\"", rendererSpy.ReceivedMarkup);
    }

    // -----------------------------------------------------------------------
    // B1a-fix [RED]: no-op (IsActive=false) designer MUST NOT fail flyer/carousel
    // -----------------------------------------------------------------------

    /// <summary>
    /// B1a passthrough fix: when the registered IVisualDesigner is inactive (IsActive=false),
    /// the processor MUST skip the designer+guardrail stage and fall through to legacy rendering.
    /// The flyer format MUST succeed — renderer IS called, no render failure is recorded.
    /// </summary>
    [Fact]
    public async Task Pipeline_InactiveDesigner_Flyer_RendererCalledAndSucceeds()
    {
        // GIVEN an inactive (no-op/B1a) designer and a normal renderer
        var rendererSpy = new SpyRenderer();
        var inactiveDesigner = new InactiveNoOpDesigner();

        var processor = BuildProcessor(
            formatKey: "flyer",
            payload: new object(),
            renderer: rendererSpy,
            designer: inactiveDesigner,
            guardrails: new FailingGuardrailValidator()); // guardrails would fail if called — proves they are NOT called

        // WHEN processing runs
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN renderer IS called (designer stage was skipped — legacy path taken)
        Assert.True(rendererSpy.WasCalled,
            "Renderer MUST be called for flyer when the designer is inactive (IsActive=false). " +
            "B1a passthrough: the no-op designer must not cause a render failure.");
    }

    /// <summary>
    /// Triangulation: same passthrough guarantee for carousel.
    /// </summary>
    [Fact]
    public async Task Pipeline_InactiveDesigner_Carousel_RendererCalledAndSucceeds()
    {
        // GIVEN an inactive designer for a carousel job
        var rendererSpy = new SpyRenderer();
        var inactiveDesigner = new InactiveNoOpDesigner();

        var processor = BuildProcessor(
            formatKey: "carousel",
            payload: new object(),
            renderer: rendererSpy,
            designer: inactiveDesigner,
            guardrails: new FailingGuardrailValidator()); // would fail if called

        // WHEN
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN renderer IS called — legacy render path used
        Assert.True(rendererSpy.WasCalled,
            "Renderer MUST be called for carousel when the designer is inactive (IsActive=false).");
    }

    /// <summary>
    /// Resilience: an ACTIVE designer returning a genuine failure (provider error, non-compliant
    /// model) MUST still produce the flyer via the deterministic brand-bound fallback. A model or
    /// provider failure must not break content generation — the flow completes for any agent.
    /// </summary>
    [Fact]
    public async Task Pipeline_ActiveDesigner_GenuineFailure_FallsBackToDeterministicComposition()
    {
        // GIVEN an active designer that returns a genuine failure
        var rendererSpy = new CapturingGeometryRenderer();
        var activeFailingDesigner = new ActiveFailingDesigner();

        var processor = BuildProcessor(
            formatKey: "flyer",
            payload: new object(),
            renderer: rendererSpy,
            designer: activeFailingDesigner,
            guardrails: new PassingGuardrailValidator());

        // WHEN
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN renderer IS called with deterministic fallback markup — the failure did not break it
        Assert.True(rendererSpy.WasCalled,
            "Renderer MUST be called — a designer/provider failure falls back to a deterministic composition.");
        Assert.NotNull(rendererSpy.ReceivedMarkup);
        Assert.Contains("class=\"composition\"", rendererSpy.ReceivedMarkup);
        Assert.Contains("linear-gradient", rendererSpy.ReceivedMarkup);
    }

    /// <summary>
    /// Happy path (Creative tier): when the designer succeeds AND the guardrail passes, the AI
    /// design MUST be used as-is — the deterministic fallback only kicks in on failure.
    /// </summary>
    [Fact]
    public async Task Pipeline_CreativeTier_DesignerAndGuardrailPass_UsesAiMarkup()
    {
        var rendererSpy = new CapturingGeometryRenderer();

        var processor = BuildProcessor(
            formatKey: "flyer",
            payload: new object(),
            renderer: rendererSpy,
            designer: new SucceedingDesigner(),
            guardrails: new PassingGuardrailValidator());

        await processor.ProcessAsync(JobId, CancellationToken.None);

        Assert.True(rendererSpy.WasCalled);
        Assert.NotNull(rendererSpy.ReceivedMarkup);
        // The SucceedingDesigner stub emits class="layer-background"; its presence proves the AI
        // markup was used rather than the deterministic fallback.
        Assert.Contains("class=\"layer-background\"", rendererSpy.ReceivedMarkup);
    }

    // -----------------------------------------------------------------------
    // [RED] Regression: designer AI call MUST run inside an active ProviderCallContext
    // -----------------------------------------------------------------------

    /// <summary>
    /// Production bug: AiVisualDesigner.ComposeAsync ran from the render loop with NO ambient
    /// ProviderCallContext, so the instrumentation decorator threw "ProviderCallContext.Current is
    /// null" and EVERY flyer/carousel failed. The processor MUST open a Begin() scope around the
    /// designer call (mirroring how the content generators scope their own provider calls).
    /// </summary>
    [Fact]
    public async Task Pipeline_ActiveDesigner_ProviderCallContextIsActiveDuringComposeCall()
    {
        // GIVEN the SAME ProviderCallContext the instrumentation decorator would read in production
        var providerCallContext = new ProviderCallContext();
        var designer = new ContextCapturingDesigner(providerCallContext);

        var processor = BuildProcessor(
            formatKey: "flyer",
            payload: new object(),
            renderer: new SpyRenderer(),
            designer: designer,
            guardrails: new PassingGuardrailValidator(),
            providerCallContext: providerCallContext);

        // WHEN processing runs
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // THEN the designer observed a non-null ProviderCallContext.Current at call time
        Assert.True(designer.WasCalled, "Designer must be invoked for an active flyer designer.");
        Assert.True(
            designer.ContextWasActive,
            "ProviderCallContext.Current MUST be non-null while the designer's AI call runs — " +
            "otherwise the instrumentation decorator throws and every flyer/carousel fails.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static BrandSnapshot BuildBrandWithForcedDefault() =>
        BrandSnapshot.FromBrandData(
            new BrandManualSnapshot(
                ColorPalette: new[] { "#FFFFFF", "#000000" },
                Logos: Array.Empty<string>(),
                BackgroundTemplates:
                [
                    new BrandBackgroundTemplateSnapshot(
                        "forced-flyer-bg",
                        "https://cdn.example.com/forced-flyer-bg.png",
                        null,
                        BackgroundTier.ForcedDefault,
                        "flyer",
                        true)
                ],
                Typography: new[] { "Inter" },
                VoiceAndTone: null,
                Notes: null),
            null, null);

    private static BrandSnapshot BuildBrandWithLegacyBackground() =>
        BrandSnapshot.FromBrandData(
            new BrandManualSnapshot(
                ColorPalette: new[] { "#FFFFFF", "#000000" },
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
                Notes: null),
            null, null);

    private ContentGenerationJobProcessor BuildProcessor(
        string formatKey,
        object payload,
        IContentRenderer renderer,
        IVisualDesigner designer,
        IVisualGuardrailValidator guardrails,
        bool brandWithForcedDefault = false,
        bool brandWithLegacyBackground = false,
        ProviderCallContext? providerCallContext = null)
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

        var brandLoader = brandWithForcedDefault
            ? (IClientBrandSnapshotLoader)new ForcedDefaultBrandLoader()
            : brandWithLegacyBackground
                ? new LegacyBackgroundBrandLoader()
            : new EmptyBrandLoader();

        var brand = brandWithForcedDefault
            ? BuildBrandWithForcedDefault()
            : brandWithLegacyBackground
                ? BuildBrandWithLegacyBackground()
                : BrandSnapshot.FromBrandData(null, null, null);

        return new ContentGenerationJobProcessor(
            jobs: new SimpleJobRepository(fakeJob),
            analyses: new SimpleAnalysisRepository(analysisRecord),
            orchestrator: new SimpleOrchestrator(formatKey, payload, brand),
            formatResults: new SimpleFormatResultRepository(),
            brandLoader: brandLoader,
            composer: new RasterCompositionService(formatKey),
            renderer: renderer,
            packageBuilder: new SimplePackageBuilder(),
            packageStorage: new SimplePackageStorage(),
            packages: new SimplePackageRepository(),
            assetStorage: null,
            assetRepository: null,
            designer: designer,
            guardrails: guardrails,
            providerCallContext: providerCallContext,
            logger: NullLogger<ContentGenerationJobProcessor>.Instance);
    }
}

// -----------------------------------------------------------------------
// Spy / stub implementations
// -----------------------------------------------------------------------

file sealed class SpyRenderer : IContentRenderer
{
    public bool WasCalled { get; private set; }

    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        WasCalled = true;
        var bytes = System.Text.Encoding.UTF8.GetBytes($"fake-{spec.FormatKey}-content");
        var asset = RenderedAsset.Create(spec.FormatKey, "text/plain", $"{spec.FormatKey}.txt", bytes);
        return Task.FromResult(Result<RenderedAsset>.Success(asset));
    }
}

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

file sealed class SpyDesigner : IVisualDesigner
{
    public BackgroundSelection? ReceivedSelection { get; private set; }

    /// <inheritdoc />
    public bool IsActive => true; // Active: participates in the designer+guardrail stage

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken)
    {
        ReceivedSelection = backgroundSelection;
        var output = new VisualDesignOutput(
            Markup: BuildValidMarkup(),
            PromptVersionKey: "visual-designer/v1",
            IsValid: false);
        return Task.FromResult(Result.Success(output));
    }

    private static string BuildValidMarkup() =>
        """<div class="composition" data-format="flyer" style="width:1080px;height:1920px;"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;">Test</h1></div></div>""";
}

/// <summary>
/// Active designer that records whether <see cref="ProviderCallContext.Current"/> was set at the
/// moment <c>ComposeAsync</c> ran — proving the processor opened a provider-call scope around it.
/// Returns valid markup so the pipeline proceeds to the guardrail/render stages.
/// </summary>
file sealed class ContextCapturingDesigner(ProviderCallContext context) : IVisualDesigner
{
    public bool WasCalled { get; private set; }
    public bool ContextWasActive { get; private set; }

    public bool IsActive => true;

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken)
    {
        WasCalled = true;
        ContextWasActive = context.Current is not null;
        var output = new VisualDesignOutput(
            Markup: """<div class="composition" data-format="flyer" style="width:1080px;height:1920px;"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;">Test</h1></div></div>""",
            PromptVersionKey: "visual-designer/v1",
            IsValid: false);
        return Task.FromResult(Result.Success(output));
    }
}

file sealed class SucceedingDesigner : IVisualDesigner
{
    /// <inheritdoc />
    public bool IsActive => true; // Active: participates in the designer+guardrail stage

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken)
    {
        var output = new VisualDesignOutput(
            Markup: """<div class="composition" data-format="flyer" style="width:1080px;height:1920px;"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"><h1 class="slot" data-slot="headline" data-max-lines="3" style="font-family:Inter;color:#FFFFFF;">Test</h1></div></div>""",
            PromptVersionKey: "visual-designer/v1",
            IsValid: false);
        return Task.FromResult(Result.Success(output));
    }
}

file sealed class FailingGuardrailValidator : IVisualGuardrailValidator
{
    public Result<VisualDesignOutput> Validate(VisualDesignOutput output, BrandManualSnapshot brand, VisualCompositionSpec spec) =>
        Result.Failure<VisualDesignOutput>(VisualGuardrailErrors.OffPalette("#FF0000"));
}

file sealed class PassingGuardrailValidator : IVisualGuardrailValidator
{
    public Result<VisualDesignOutput> Validate(VisualDesignOutput output, BrandManualSnapshot brand, VisualCompositionSpec spec) =>
        Result.Success(output.WithValidated());
}

file sealed class NoOpDesigner : IVisualDesigner
{
    /// <inheritdoc />
    public bool IsActive => true; // Active test stub (participates in pipeline; returns success)

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new VisualDesignOutput(
            Markup: string.Empty,
            PromptVersionKey: "visual-designer/v1",
            IsValid: false)));
}

file sealed class NoOpGuardrailValidator : IVisualGuardrailValidator
{
    public Result<VisualDesignOutput> Validate(VisualDesignOutput output, BrandManualSnapshot brand, VisualCompositionSpec spec) =>
        Result.Success(output.WithValidated());
}

/// <summary>
/// B1a-fix RED: Simulates the no-op designer with IsActive=false.
/// The pipeline must skip the designer+guardrail stage entirely and fall through to legacy rendering.
/// (Requires IVisualDesigner.IsActive to exist — this is the RED compile gate.)
/// </summary>
file sealed class InactiveNoOpDesigner : IVisualDesigner
{
    /// <inheritdoc />
    public bool IsActive => false;

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken) =>
        // Should never be called when IsActive=false — but returns failure just in case.
        Task.FromResult(
            Result.Failure<VisualDesignOutput>(
                Error.Failure("visual-designer.no-op", "B1a no-op — should not be called when IsActive=false.")));
}

/// <summary>
/// B1a-fix triangulation: simulates an ACTIVE designer that returns a genuine failure.
/// This verifies that the fix does NOT accidentally suppress real designer errors.
/// </summary>
file sealed class ActiveFailingDesigner : IVisualDesigner
{
    /// <inheritdoc />
    public bool IsActive => true;

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            Result.Failure<VisualDesignOutput>(
                Error.Failure("visual-designer.real-failure", "Genuine designer failure — renderer must not be called.")));
}

file sealed class EmptyBrandLoader : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(BrandSnapshot.FromBrandData(null, null, null));
}

file sealed class ForcedDefaultBrandLoader : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default)
    {
        var brand = new BrandManualSnapshot(
            ColorPalette: new[] { "#FFFFFF", "#000000" },
            Logos: Array.Empty<string>(),
            BackgroundTemplates:
            [
                new BrandBackgroundTemplateSnapshot(
                    "forced-flyer-bg",
                    "https://cdn.example.com/forced-flyer-bg.png",
                    null,
                    BackgroundTier.ForcedDefault,
                    "flyer",
                    true)
            ],
            Typography: new[] { "Inter" },
            VoiceAndTone: null,
            Notes: null);
        return Task.FromResult(BrandSnapshot.FromBrandData(brand, null, null));
    }
}

file sealed class LegacyBackgroundBrandLoader : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default)
    {
        var brand = new BrandManualSnapshot(
            ColorPalette: new[] { "#FFFFFF", "#000000" },
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

/// <summary>Composition service that returns a Raster spec for designed formats.</summary>
file sealed class RasterCompositionService(string designedFormat) : IVisualCompositionService
{
    private static readonly CompositionPalette DefaultPalette = new("#333", "#666", "#999", "#FFF", "#EEE");
    private static readonly CompositionTypography DefaultTypo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions DefaultDim = new(1080, 1920, 96);

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct)
    {
        var spec = BuildSpec(formatKey);
        return Task.FromResult(spec);
    }

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<VisualCompositionSpec>>([BuildSpec(formatKey)]);
    }

    private VisualCompositionSpec BuildSpec(string formatKey)
    {
        // Designed formats (flyer, carousel) get Raster kind so the pipeline routes them through the designer.
        var outputKind = string.Equals(formatKey, designedFormat, StringComparison.OrdinalIgnoreCase)
            ? CompositionOutputKind.Raster
            : CompositionOutputKind.Text;

        return new VisualCompositionSpec(
            FormatKey: formatKey,
            LayoutType: "story-flyer",
            Palette: DefaultPalette,
            Typography: DefaultTypo,
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = "Test" },
            Dimensions: DefaultDim)
        with { OutputKind = outputKind, TextBody = outputKind == CompositionOutputKind.Text ? "fake body" : null };
    }

    public IReadOnlyList<string> GetInferredFields() => [];
}

file sealed class SimpleJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class SimpleAnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
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

file sealed class SimpleFormatResultRepository : IContentGenerationFormatResultRepository
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

file sealed class SimpleOrchestrator(string formatKey, object payload, BrandSnapshot? brand = null) : IContentGenerationOrchestrator
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

file sealed class SimplePackageBuilder : IGeneratedPackageBuilder
{
    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-zip"));
        var manifest = new GeneratedPackageManifest(
            new Dictionary<string, FormatAssetMetadata>(),
            [],
            [],
            0,
            input.ClientName ?? "test-client");
        return Task.FromResult(new BuiltPackage(stream, stream.Length, manifest, "test-package.zip"));
    }
}

file sealed class SimplePackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake-storage://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class SimplePackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) => Task.FromResult<GeneratedPackage?>(null);
}
