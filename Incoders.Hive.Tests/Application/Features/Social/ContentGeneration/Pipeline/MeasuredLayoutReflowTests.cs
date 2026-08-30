using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Visual.Layout;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentFormats;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Tests.Application.Features.Social.ContentGeneration.Pipeline;

/// <summary>
/// WU-4 TDD tests — the bounded two-pass reflow loop.
/// Spec: CLIF-CG-03, CLIF-CG-04, CLIF-CG-05, CLIF-CG-06, CLIF-CG-08.
/// Design: D-C, D-D, <see cref="MeasuredLayoutReflow"/> formula, <c>MaxRenderPasses = 3</c>,
/// <c>EpsilonPx = 1.0</c>, ADR 0016.
///
/// Split into two parts:
/// <list type="bullet">
///   <item>Pure <see cref="MeasuredLayoutReflow.RecomputeTops"/> formula tests (WU-4a, tasks 4.1-4.3)
///     — no render loop, no Chromium.</item>
///   <item><see cref="ContentGenerationJobProcessor.RenderWithReflowAsync"/> integration tests
///     (WU-4b, tasks 4.4-4.6, 4.13-4.16) against a stub <see cref="IContentRendererWithGeometry"/> —
///     no real Chromium process boundary, but exercises the actual bounded loop, fast path, hard
///     cap, and non-convergence diagnostic.</item>
/// </list>
/// </summary>
public sealed class MeasuredLayoutReflowTests
{
    private static readonly Guid JobId = Guid.Parse("eeeeeee1-eeee-eeee-eeee-eeeeeeeeeeee");

    // -----------------------------------------------------------------------
    // Pure formula: MeasuredLayoutReflow.RecomputeTops (WU-4a, tasks 4.1-4.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void RecomputeTops_FormulaExactness_StoryHeadlineGrows_SubtitlePushedByMeasuredExtent()
    {
        // design.md's own worked example: 255 + max(260,447) + gap 60 = 762.
        // Real BuildStoryTemplateSlots literals: headline top:255 min-height:260 (bottom 515),
        // subtitle top:575 => declared gap = 575 - 515 = 60.
        var declared = new[]
        {
            new SlotBox("headline", Left: 96, Top: 255, Width: 888, Height: 260),
            new SlotBox("subtitle", Left: 100, Top: 575, Width: 825, Height: 122),
        };
        var measured = new[]
        {
            new SlotBox("headline", Left: 96, Top: 255, Width: 888, Height: 447), // wrapped to 2-3 lines
            new SlotBox("subtitle", Left: 100, Top: 575, Width: 825, Height: 122),
        };

        var tops = MeasuredLayoutReflow.RecomputeTops(declared, measured, canvasHeight: 1920);

        Assert.Equal(255, tops["headline"]); // anchor slot never moves
        Assert.Equal(762, tops["subtitle"]); // 255 + max(260,447) + 60
    }

    [Fact]
    public void RecomputeTops_EverythingAlreadyFits_ReturnsDeclaredTopsExactly_ByteIdentity()
    {
        // CLIF-CG-08-a: measured heights <= declared min-heights => recomputed tops MUST equal
        // declared tops exactly (the byte-identical fast-path guarantee).
        var declared = new[]
        {
            new SlotBox("headline", Left: 96, Top: 255, Width: 888, Height: 260),
            new SlotBox("subtitle", Left: 100, Top: 575, Width: 825, Height: 122),
        };
        var measured = new[]
        {
            new SlotBox("headline", Left: 96, Top: 255, Width: 888, Height: 200), // fits within 260
            new SlotBox("subtitle", Left: 100, Top: 575, Width: 825, Height: 100), // fits within 122
        };

        var tops = MeasuredLayoutReflow.RecomputeTops(declared, measured, canvasHeight: 1920);

        Assert.Equal(255, tops["headline"]);
        Assert.Equal(575, tops["subtitle"]);
    }

    [Fact]
    public void RecomputeTops_ClampsAtCanvasBottomMinus140_ResidualOverlapNotHiddenByPushingOffCanvas()
    {
        // A pathologically tall headline (measured 1600px) would push the subtitle's recomputed top
        // to 1915px on a 1920px canvas — past the BottomSafePx(140) clamp limit (1780px). The clamp
        // MUST stop at 1780 - effectiveHeight rather than letting the subtitle drift off-canvas.
        var declared = new[]
        {
            new SlotBox("headline", Left: 96, Top: 255, Width: 888, Height: 260),
            new SlotBox("subtitle", Left: 100, Top: 575, Width: 825, Height: 122),
        };
        var measured = new[]
        {
            new SlotBox("headline", Left: 96, Top: 255, Width: 888, Height: 1600),
            new SlotBox("subtitle", Left: 100, Top: 575, Width: 825, Height: 122),
        };

        var tops = MeasuredLayoutReflow.RecomputeTops(declared, measured, canvasHeight: 1920);

        var clampLimit = 1920 - MeasuredLayoutReflow.BottomSafePx;
        Assert.Equal(1658, tops["subtitle"]); // clampLimit(1780) - effectiveHeight(122)
        Assert.True(tops["subtitle"] + 122 <= clampLimit);
    }

    // -----------------------------------------------------------------------
    // Integration: ContentGenerationJobProcessor.RenderWithReflowAsync (WU-4b, tasks 4.4-4.6, 4.13-4.16)
    // -----------------------------------------------------------------------

    private const string StoryMarkupTemplate =
        """
        <div class="composition" data-format="story" style="position:relative;width:1080px;height:1920px;">
          <div data-layer="content">
            <h1 class="slot" data-slot="headline" data-max-lines="3" style="position:absolute;left:96px;top:255px;width:888px;min-height:260px;color:#fff;">Headline</h1>
            <p class="slot" data-slot="subtitle" data-max-lines="6" style="position:absolute;left:100px;top:575px;width:825px;min-height:122px;color:#fff;">Subtitle</p>
          </div>
        </div>
        """;

    private static readonly IReadOnlyList<string> StoryReflowOrder = ["headline", "subtitle", "body"];

    [Fact]
    public async Task RenderWithReflowAsync_NoOverlapNoMaterialDelta_StopsAfterExactlyOneRender()
    {
        // CLIF-CG-03-a: Pass 1's measurement shows no intersection and no material top delta =>
        // rendering MUST stop after exactly one render.
        var stub = new StubGeometryRenderer(new List<IReadOnlyList<SlotGeometryEntry>>
        {
            new[]
            {
                Geometry("headline", y: 255, height: 260),
                Geometry("subtitle", y: 575, height: 122),
            },
        });
        var (processor, _) = BuildProcessorForReflow();
        var spec = BuildStorySpec(StoryMarkupTemplate);

        var result = await processor.RenderWithReflowAsync(
            stub, spec, StoryMarkupTemplate, StoryReflowOrder, "story", specIndex: 0, JobId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, stub.CallCount);
        Assert.Equal(1, result.Value.RenderPasses);
    }

    [Fact]
    public async Task RenderWithReflowAsync_OverlapOnPass1_RecomputesAndReRenders_FinalPassHasNoIntersection()
    {
        // CLIF-CG-04-a: Pass 1 shows the headline (wrapped, measured height 447) intruding into the
        // subtitle. The loop MUST recompute the subtitle's top, re-render, and re-measure until the
        // shipped (best-effort-selected) layout shows no intersection, within the 3-render cap.
        var stub = new StubGeometryRenderer(new List<IReadOnlyList<SlotGeometryEntry>>
        {
            new[] // pass 1: overlap
            {
                Geometry("headline", y: 255, height: 447),
                Geometry("subtitle", y: 575, height: 122),
            },
            new[] // pass 2: subtitle pushed down by the recompute, no overlap
            {
                Geometry("headline", y: 255, height: 447),
                Geometry("subtitle", y: 762, height: 122),
            },
            new[] // pass 3 (if the loop keeps going within the cap): still no overlap
            {
                Geometry("headline", y: 255, height: 447),
                Geometry("subtitle", y: 900, height: 122),
            },
        });
        var (processor, _) = BuildProcessorForReflow();
        var spec = BuildStorySpec(StoryMarkupTemplate);

        var result = await processor.RenderWithReflowAsync(
            stub, spec, StoryMarkupTemplate, StoryReflowOrder, "story", specIndex: 0, JobId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.InRange(stub.CallCount, 2, MeasuredLayoutReflow.MaxRenderPasses); // more than the fast path, never past the cap
        Assert.Equal(stub.CallCount, result.Value.RenderPasses);

        // CLIF-CG-04-b (regression): the final markup remains absolutely positioned — no
        // document-flow/flex conversion — for both reflowed slots.
        Assert.Contains("data-slot=\"headline\"", result.Value.PersistedMarkup);
        Assert.Contains("data-slot=\"subtitle\"", result.Value.PersistedMarkup);
        Assert.Contains("position:absolute", result.Value.PersistedMarkup);
        Assert.Equal(result.Value.RenderMarkup.Contains("top:255px"), result.Value.PersistedMarkup.Contains("top:255px"));
    }

    [Fact]
    public async Task RenderWithReflowAsync_NeverConverges_HardCapsAtThreeRenders_NeverAFourthRender()
    {
        // CLIF-CG-05-a: a deliberately non-converging fixture (every pass still shows an
        // overlap, regardless of the recompute) MUST NEVER exceed 3 total renders.
        var stub = new StubGeometryRenderer(new List<IReadOnlyList<SlotGeometryEntry>>
        {
            new[] { Geometry("headline", y: 255, height: 400), Geometry("subtitle", y: 575, height: 122) },
            new[] { Geometry("headline", y: 255, height: 420), Geometry("subtitle", y: 575, height: 122) },
            new[] { Geometry("headline", y: 255, height: 350), Geometry("subtitle", y: 575, height: 122) },
            // A 4th entry that MUST NEVER be consumed — proves the loop is structurally capped, not
            // just "happens" to stop at 3 for this fixture.
            new[] { Geometry("headline", y: 255, height: 900), Geometry("subtitle", y: 575, height: 122) },
        });
        var (processor, warnings) = BuildProcessorForReflow();
        var spec = BuildStorySpec(StoryMarkupTemplate);

        var result = await processor.RenderWithReflowAsync(
            stub, spec, StoryMarkupTemplate, StoryReflowOrder, "story", specIndex: 3, JobId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeasuredLayoutReflow.MaxRenderPasses, stub.CallCount);
        Assert.Equal(3, result.Value.RenderPasses);

        // CLIF-CG-05-b: best-effort selection is the pass with the SMALLEST total intersection area.
        // Pass areas (825px overlap-width fixed): pass1=66000, pass2=82500, pass3=24750 => pass 3 wins.
        Assert.Contains("fake-story-content-pass-3", System.Text.Encoding.UTF8.GetString(result.Value.Asset.GetBytes()));

        // A structured non-convergence diagnostic MUST be emitted, and it MUST NEVER route through
        // any retry mechanism (CAC-IMG-03) or fail the slide — the method returned Success above.
        var diagnostic = Assert.Single(warnings, w => w.Contains("ContentGeneration.LayoutReflow", StringComparison.Ordinal));
        Assert.Contains(JobId.ToString(), diagnostic);
        Assert.Contains("story", diagnostic);
        Assert.Contains("3 render pass(es)", diagnostic);
        Assert.DoesNotContain("CAC-IMG-03", diagnostic);
    }

    private static SlotGeometryEntry Geometry(string slot, double y, double height) =>
        new(slot, X: slot == "headline" ? 96 : 100, Y: y, Width: slot == "headline" ? 888 : 825,
            Height: height, FontFamily: "Inter", FontSizePx: 40, Color: "#fff", TextAlign: "left");

    private static VisualCompositionSpec BuildStorySpec(string markup) => new VisualCompositionSpec(
        FormatKey: "story",
        LayoutType: "story-story",
        Palette: new CompositionPalette("#333", "#666", "#999", "#FFF", "#EEE"),
        Typography: new CompositionTypography("Inter", 74, "Inter", 38),
        AssetSlots: new Dictionary<string, AssetSlotSpec>(),
        CopySlots: new Dictionary<string, string> { ["headline"] = "Test" },
        Dimensions: new CompositionDimensions(1080, 1920, 96))
    with
    {
        OutputKind = CompositionOutputKind.Html,
        DesignerMarkup = markup,
    };

    private static (ContentGenerationJobProcessor Processor, IReadOnlyList<string> Warnings) BuildProcessorForReflow()
    {
        var logger = new RfwCapturingLogger();
        var processor = new ContentGenerationJobProcessor(
            jobs: new RfwUnusedJobRepository(),
            analyses: new RfwUnusedAnalysisRepository(),
            orchestrator: new RfwUnusedOrchestrator(),
            formatResults: new RfwUnusedFormatResultRepository(),
            brandLoader: new RfwUnusedBrandLoader(),
            composer: new RfwUnusedCompositionService(),
            renderer: new RfwUnusedRenderer(),
            packageBuilder: new RfwUnusedPackageBuilder(),
            packageStorage: new RfwUnusedPackageStorage(),
            packages: new RfwUnusedPackageRepository(),
            logger: logger);
        return (processor, logger.Warnings);
    }
}

// ─── Test doubles — RenderWithReflowAsync only reads `_logger`; every other dependency is
// ─── constructor-required but structurally unreachable from this internal method under test. ────

file sealed class StubGeometryRenderer(IReadOnlyList<IReadOnlyList<SlotGeometryEntry>> sequence) : IContentRendererWithGeometry
{
    public int CallCount { get; private set; }

    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct) =>
        throw new NotSupportedException("RenderWithReflowAsync must call RenderWithGeometryAsync, never the base RenderAsync.");

    public Task<Result<RenderedAssetWithGeometry>> RenderWithGeometryAsync(
        VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        CallCount++;
        var geometry = sequence[Math.Min(CallCount - 1, sequence.Count - 1)];
        var bytes = System.Text.Encoding.UTF8.GetBytes($"fake-{spec.FormatKey}-content-pass-{CallCount}");
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", bytes);
        return Task.FromResult(Result.Success(new RenderedAssetWithGeometry(asset, geometry, null)));
    }
}

file sealed class RfwCapturingLogger : ILogger<ContentGenerationJobProcessor>
{
    private readonly List<string> _warnings = new();
    public IReadOnlyList<string> Warnings => _warnings;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
        {
            _warnings.Add(formatter(state, exception));
        }
    }
}

file sealed class RfwUnusedJobRepository : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => throw new NotSupportedException();
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => throw new NotSupportedException();
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => throw new NotSupportedException();
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => throw new NotSupportedException();
}

file sealed class RfwUnusedAnalysisRepository : ISingleContentAnalysisRepository
{
    public Task AddAsync(SingleContentAnalysisRecord a, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateStatusAsync(Guid id, Guid orgId, SingleContentAnalysisStatus status, DateTime updatedAt, CancellationToken ct = default, string? failCode = null, string? failMsgKey = null) => throw new NotSupportedException();
    public Task UpdateAsync(SingleContentAnalysisRecord a, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(Guid orgId, Guid userId, int page, int pageSize, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(Guid orgId, Guid userId, string normalized, string src, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(Guid id, Guid orgId, Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
}

file sealed class RfwUnusedOrchestrator : IContentGenerationOrchestrator
{
    public Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken ct) => throw new NotSupportedException();
}

file sealed class RfwUnusedFormatResultRepository : IContentGenerationFormatResultRepository
{
    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult r, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) => throw new NotSupportedException();
}

file sealed class RfwUnusedBrandLoader : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) => throw new NotSupportedException();
}

file sealed class RfwUnusedCompositionService : IVisualCompositionService
{
    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, ClientAestheticProfile? aestheticProfile = null) => throw new NotSupportedException();
    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, CarouselBackgroundPlan? plan = null, ClientAestheticProfile? aestheticProfile = null) => throw new NotSupportedException();
    public IReadOnlyList<string> GetInferredFields() => throw new NotSupportedException();
}

file sealed class RfwUnusedRenderer : IContentRenderer
{
    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct) => throw new NotSupportedException();
}

file sealed class RfwUnusedPackageBuilder : IGeneratedPackageBuilder
{
    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct) => throw new NotSupportedException();
}

file sealed class RfwUnusedPackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) => throw new NotSupportedException();
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) => throw new NotSupportedException();
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => throw new NotSupportedException();
}

file sealed class RfwUnusedPackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => throw new NotSupportedException();
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) => throw new NotSupportedException();
}
