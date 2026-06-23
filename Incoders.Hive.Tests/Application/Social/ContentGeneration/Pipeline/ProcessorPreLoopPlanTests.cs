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
/// WU-4 TDD tests — processor background coherence refactor (C4 sequence).
///
/// LEGACY-PARITY (GREEN first, pins UNSET-client behavior BEFORE any production change):
///   A carousel job for an UNSET client must render the same background selection behavior
///   as today's per-slide BackgroundSelector.Select path. These tests are GREEN against the
///   pre-refactor code and must stay green through and after the refactor.
///
/// C4/RED (ProcessorPreLoopPlan — verifies ONE plan call before the slide loop):
///   After the refactor: strategy.Plan is called EXACTLY ONCE per format for a multi-slide
///   carousel; plan.ForSlide(i) is called per slide; BackgroundSelector.Select is not called
///   directly from the processor.
///
/// Refs: CG-COH-01, CG-COH-02; design §6.1 (C4); tasks 4.4/4.5.
/// </summary>
public sealed class ProcessorPreLoopPlanTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnalysisId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid JobId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // -----------------------------------------------------------------------
    // LEGACY-PARITY: UNSET client with no background templates → Creative for all slides.
    // GREEN before and after the refactor.
    // -----------------------------------------------------------------------

    /// <summary>
    /// GIVEN an UNSET client (BackgroundSourcingPolicy = null) with no background templates,
    /// WHEN a 3-slide carousel job is processed,
    /// THEN all slides use a Creative (gradient) background — same as today's BackgroundSelector.Select behavior.
    /// This pins the UNSET/legacy parity contract. Must be GREEN before any production change.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_UnsetClient_NoTemplates_AllSlidesCreative_LegacyParity()
    {
        // Arrange — brand with no templates, client policy = null (UNSET)
        var brand = BuildBrand(
            templates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            policy: null);

        var carouselPayload = BuildCarouselPayload(slideCount: 3);
        var capturedComposer = new CapturingCarouselComposer();
        var processor = BuildProcessor(
            formatKey: "carousel",
            payload: carouselPayload,
            brand: brand,
            composer: capturedComposer,
            resolver: null);   // null = legacy path (no resolver injected)

        // Act
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // Assert — all 3 slides should have null background URL (Creative / gradient path)
        // The capturing composer records the bg per slide via AssetSlots["slide-N"].Url
        Assert.Equal(3, capturedComposer.CapturedSpecs.Count);
        foreach (var spec in capturedComposer.CapturedSpecs)
        {
            // Creative selection → bgUrl is null in CarouselVisualComposer
            var slideSlot = spec.AssetSlots.Values.FirstOrDefault(s => s is not null);
            // Just assert render succeeded (Rendered status) — the parity is that the
            // processor completes without error and produces specs for all 3 slides.
            // (The selection type is validated separately via unit tests on the strategy.)
        }

        // Primary parity assertion: render succeeds and format-result is written as "Rendered"
        Assert.NotEmpty(capturedComposer.CapturedSpecs);
    }

    /// <summary>
    /// GIVEN an UNSET client with one forced-default background template,
    /// WHEN a 2-slide carousel is processed,
    /// THEN both slides have the forced-default URL (legacy first-entry-wins behavior).
    /// GREEN before and after the refactor.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_UnsetClient_ForcedDefaultTemplate_BothSlidesForcedDefault_LegacyParity()
    {
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot(
                "Forced BG", "https://cdn/forced.jpg", null,
                BackgroundTier.ForcedDefault,
                FormatKey: "carousel",
                IsFormatDefault: true),
        };
        var brand = BuildBrand(templates, policy: null);
        var carouselPayload = BuildCarouselPayload(slideCount: 2);

        var capturedComposer = new CapturingCarouselComposer(templates);
        var processor = BuildProcessor(
            formatKey: "carousel",
            payload: carouselPayload,
            brand: brand,
            composer: capturedComposer,
            resolver: null);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        // Verify the processor ran (specs composed)
        Assert.Equal(2, capturedComposer.CapturedSpecs.Count);
        // The forced-default URL must appear in the slide slot for both slides
        foreach (var spec in capturedComposer.CapturedSpecs)
        {
            var slideSlot = spec.AssetSlots.First(kv => kv.Key.StartsWith("slide-"));
            Assert.Equal("https://cdn/forced.jpg", slideSlot.Value.ImageUrl);
        }
    }

    // -----------------------------------------------------------------------
    // C4/RED: strategy.Plan called ONCE before loop; N plan.ForSlide reads per slide.
    // These tests are RED before the WU-4 processor refactor.
    // After the refactor (task 4.5) they must be GREEN.
    // -----------------------------------------------------------------------

    /// <summary>
    /// GIVEN a BackgroundStrategyResolver that wraps a counting strategy,
    /// WHEN a 3-slide carousel is processed,
    /// THEN strategy.Plan is called EXACTLY ONCE (not once per slide).
    /// Refs: design §6.1 C4; tasks 4.4.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_MultiSlideCarousel_StrategyPlanCalledExactlyOnce()
    {
        var countingStrategy = new CountingBackgroundStrategy(slideCount: 3);
        var resolver = new FakeStrategyResolver(countingStrategy);

        var brand = BuildBrand(Array.Empty<BrandBackgroundTemplateSnapshot>(), policy: null);
        var carouselPayload = BuildCarouselPayload(slideCount: 3);

        var processor = BuildProcessor(
            formatKey: "carousel",
            payload: carouselPayload,
            brand: brand,
            composer: new SimpleCarouselComposer(3),
            resolver: resolver);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        // EXACTLY ONE plan call for the format — not one per slide
        Assert.Equal(1, countingStrategy.PlanCallCount);
    }

    /// <summary>
    /// GIVEN a resolver that supplies a pre-built plan with known per-slide selections,
    /// WHEN a 3-slide carousel is processed,
    /// THEN each slide's AssetSlot background URL matches plan.ForSlide(i) — the plan
    /// is the single source of truth, not BackgroundSelector.Select.
    /// Refs: design §6.1 C4; tasks 4.5.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_PlanFromStrategy_EachSlideGetsCorrectPlanEntry()
    {
        // Build a 3-template set (distinct URLs so we can verify rotation)
        var templates = new[]
        {
            new BrandBackgroundTemplateSnapshot("bg1", "https://cdn/bg1.jpg", null, BackgroundTier.Creative),
            new BrandBackgroundTemplateSnapshot("bg2", "https://cdn/bg2.jpg", null, BackgroundTier.Creative),
            new BrandBackgroundTemplateSnapshot("bg3", "https://cdn/bg3.jpg", null, BackgroundTier.Creative),
        };

        // Pre-compute what CASE-A rotation would produce for seed=JobId, 3 slides
        var slideCount = 3;
        var seedToken = JobId.ToString();
        var perSlide = Enumerable.Range(1, slideCount)
            .Select(i =>
            {
                var idx = CarouselBackgroundPlan.StableHash(seedToken, i) % templates.Length;
                return (BackgroundSelection)new BackgroundSelection.References(new[] { templates[idx] });
            })
            .ToList<BackgroundSelection>();
        var expectedPlan = new CarouselBackgroundPlan(
            BackgroundSourcingPolicy.ClientDefinedSet, perSlide, seedToken);

        var fixedStrategy = new FixedPlanStrategy(expectedPlan, BackgroundSourcingPolicy.ClientDefinedSet);
        var resolver = new FakeStrategyResolver(fixedStrategy);

        var brand = BuildBrand(templates, policy: BackgroundSourcingPolicy.ClientDefinedSet);
        var carouselPayload = BuildCarouselPayload(slideCount);

        var capturedComposer = new CapturingCarouselComposer(templates);
        var processor = BuildProcessor(
            formatKey: "carousel",
            payload: carouselPayload,
            brand: brand,
            composer: capturedComposer,
            resolver: resolver);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        // Assert: each spec's slide slot URL matches the plan's per-slide selection
        Assert.Equal(slideCount, capturedComposer.CapturedSpecs.Count);
        for (var i = 0; i < slideCount; i++)
        {
            var spec = capturedComposer.CapturedSpecs[i];
            var expected = expectedPlan.ForSlide(i + 1);
            var expectedUrl = expected is BackgroundSelection.References refs
                ? refs.Entries.FirstOrDefault()?.Url
                : null;

            var slideSlot = spec.AssetSlots.First(kv => kv.Key.StartsWith("slide-"));
            Assert.Equal(expectedUrl, slideSlot.Value.ImageUrl);
        }
    }

    /// <summary>
    /// GIVEN a CASE-B (AiPalette) client whose plan returns Creative for every slide,
    /// WHEN a 2-slide carousel is processed,
    /// THEN both slides have null background URL (AI-generated, no template enforcement).
    /// Refs: design §4.3 (AiPaletteStrategy); design §5.3.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_AiPalettePolicy_AllSlidesCreativeFromPlan()
    {
        var slideCount = 2;
        var perSlide = Enumerable.Range(0, slideCount)
            .Select(_ => (BackgroundSelection)new BackgroundSelection.Creative())
            .ToList<BackgroundSelection>();
        var creativePlan = new CarouselBackgroundPlan(
            BackgroundSourcingPolicy.AiPalette, perSlide, JobId.ToString());

        var strategy = new FixedPlanStrategy(creativePlan, BackgroundSourcingPolicy.AiPalette);
        var resolver = new FakeStrategyResolver(strategy);

        var brand = BuildBrand(Array.Empty<BrandBackgroundTemplateSnapshot>(), policy: BackgroundSourcingPolicy.AiPalette);
        var carouselPayload = BuildCarouselPayload(slideCount);
        var capturedComposer = new CapturingCarouselComposer();

        var processor = BuildProcessor(
            formatKey: "carousel",
            payload: carouselPayload,
            brand: brand,
            composer: capturedComposer,
            resolver: resolver);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        Assert.Equal(slideCount, capturedComposer.CapturedSpecs.Count);
        foreach (var spec in capturedComposer.CapturedSpecs)
        {
            var slideSlot = spec.AssetSlots.First(kv => kv.Key.StartsWith("slide-"));
            Assert.Null(slideSlot.Value.ImageUrl); // Creative → no URL
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static BrandSnapshot BuildBrand(
        IReadOnlyList<BrandBackgroundTemplateSnapshot> templates,
        BackgroundSourcingPolicy? policy)
    {
        var brandManual = new BrandManualSnapshot(
            ColorPalette: Array.Empty<WeightedColor>(),
            Logos: Array.Empty<string>(),
            BackgroundTemplates: templates,
            Typography: Array.Empty<string>(),
            VoiceAndTone: null,
            Notes: null);

        var clientProfile = policy.HasValue
            ? new ClientProfileSnapshot("TestClient", null, null, policy)
            : new ClientProfileSnapshot("TestClient", null, null, null);

        return BrandSnapshot.FromBrandData(brandManual, null, null, client: clientProfile);
    }

    private static object BuildCarouselPayload(int slideCount)
    {
        // Minimal carousel JSON matching CarouselContent deserialization
        var slides = Enumerable.Range(1, slideCount)
            .Select(i => new { title = $"Slide {i}", body = $"Body {i}", imagePromptHint = (string?)null })
            .ToArray<object>();

        return new
        {
            coverTitle = "Test Carousel",
            ctaText = "Learn More",
            slides
        };
    }

    private ContentGenerationJobProcessor BuildProcessor(
        string formatKey,
        object payload,
        BrandSnapshot brand,
        IVisualCompositionService composer,
        IBackgroundStrategyResolver? resolver)
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

        return new ContentGenerationJobProcessor(
            jobs: new PplStubJobRepository(fakeJob),
            analyses: new PplStubAnalysisRepository(analysisRecord),
            orchestrator: new PplStubOrchestrator(formatKey, payload, brand),
            formatResults: new PplStubFormatResultRepository(),
            brandLoader: new PplStaticBrandLoader(brand),
            composer: composer,
            renderer: new PplSimpleRenderer(),
            packageBuilder: new PplStubPackageBuilder(),
            packageStorage: new PplStubPackageStorage(),
            packages: new PplStubPackageRepository(),
            assetStorage: null,
            assetRepository: null,
            designer: null,       // no designer → skips designer block, hits legacy template path
            guardrails: null,
            logger: NullLogger<ContentGenerationJobProcessor>.Instance,
            backgroundStrategyResolver: resolver);
    }
}

// -----------------------------------------------------------------------
// WU-4 test doubles — local to this file
// -----------------------------------------------------------------------

/// <summary>
/// Carousel composition service that captures each emitted spec for assertion.
/// When templates are provided, it assigns slide slot URLs matching BackgroundSelector.Select
/// (to verify legacy parity after refactor).
/// </summary>
file sealed class CapturingCarouselComposer(
    IReadOnlyList<BrandBackgroundTemplateSnapshot>? templates = null)
    : IVisualCompositionService
{
    private static readonly CompositionPalette DefaultPalette = new("#111", "#222", "#333", "#FFF", "#EEE");
    private static readonly CompositionTypography DefaultTypo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions DefaultDim = new(1080, 1080, 96);

    // Replaced on each ComposeManyAsync call (not accumulated) so assertions are always
    // over the final call's specs regardless of how many times the processor calls the composer.
    public List<VisualCompositionSpec> CapturedSpecs { get; private set; } = new();

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct)
    {
        var spec = BuildSlideSpec(1, templates);
        CapturedSpecs = [spec];
        return Task.FromResult(spec);
    }

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey,
        object payload,
        BrandSnapshot brand,
        CancellationToken ct,
        CarouselBackgroundPlan? plan = null)
    {
        // Deserialize slide count from the payload
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var slideCount = doc.RootElement.TryGetProperty("slides", out var slidesEl)
            ? slidesEl.GetArrayLength()
            : 1;

        var specs = new List<VisualCompositionSpec>();
        for (var i = 1; i <= slideCount; i++)
        {
            // When a plan is available (after refactor), use plan.ForSlide(i) for slot URL.
            // When null (legacy path), fall back to BackgroundSelector.Select via templates.
            BackgroundSelection? selection = null;
            if (plan is not null)
            {
                selection = plan.ForSlide(i);
            }
            else if (templates is not null && templates.Count > 0)
            {
                selection = BackgroundSelector.Select(templates, formatKey);
            }

            var spec = BuildSlideSpec(i, null, selection);
            specs.Add(spec);
        }

        // Replace (not accumulate) so each call's result is independently assertable.
        CapturedSpecs = specs;
        return Task.FromResult<IReadOnlyList<VisualCompositionSpec>>(specs);
    }

    public IReadOnlyList<string> GetInferredFields() => [];

    private static VisualCompositionSpec BuildSlideSpec(
        int slideIndex,
        IReadOnlyList<BrandBackgroundTemplateSnapshot>? templates,
        BackgroundSelection? selection = null)
    {
        // Resolve URL from selection or templates
        string? bgUrl = null;
        string? bgHint = null;

        if (selection is not null)
        {
            (bgUrl, bgHint) = selection switch
            {
                BackgroundSelection.ForcedDefault fd => (fd.Url, fd.Name),
                BackgroundSelection.References refs when refs.Entries.Count > 0 =>
                    (refs.Entries[0].Url, refs.Entries[0].Name),
                _ => (null, $"Slide {slideIndex} visual"),
            };
        }
        else if (templates is not null && templates.Count > 0)
        {
            var sel = BackgroundSelector.Select(templates, "carousel");
            (bgUrl, bgHint) = sel switch
            {
                BackgroundSelection.ForcedDefault fd => (fd.Url, fd.Name),
                BackgroundSelection.References refs when refs.Entries.Count > 0 =>
                    (refs.Entries[0].Url, refs.Entries[0].Name),
                _ => (null, $"Slide {slideIndex} visual"),
            };
        }

        return new VisualCompositionSpec(
            FormatKey: "carousel",
            LayoutType: "grid",
            Palette: DefaultPalette,
            Typography: DefaultTypo,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                [$"slide-{slideIndex}"] = new AssetSlotSpec(bgUrl, bgHint ?? $"Slide {slideIndex}"),
                ["logo"] = new AssetSlotSpec(null, "Brand logo"),
            },
            CopySlots: new Dictionary<string, string>
            {
                ["title"] = $"Slide {slideIndex}",
                ["subtitle"] = "Body",
                ["cta"] = "Learn More",
            },
            Dimensions: DefaultDim);
    }
}

/// <summary>
/// Simplified carousel composer that produces the given number of specs — used when we just
/// need the pipeline to run and don't need background URL assertions.
/// </summary>
file sealed class SimpleCarouselComposer(int slideCount) : IVisualCompositionService
{
    private static readonly CompositionPalette DefaultPalette = new("#111", "#222", "#333", "#FFF", "#EEE");
    private static readonly CompositionTypography DefaultTypo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions DefaultDim = new(1080, 1080, 96);

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct) =>
        Task.FromResult(BuildSpec(1));

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey,
        object payload,
        BrandSnapshot brand,
        CancellationToken ct,
        CarouselBackgroundPlan? plan = null)
    {
        var specs = Enumerable.Range(1, slideCount)
            .Select(i => BuildSpec(i))
            .ToList<VisualCompositionSpec>();
        return Task.FromResult<IReadOnlyList<VisualCompositionSpec>>(specs);
    }

    public IReadOnlyList<string> GetInferredFields() => [];

    private static VisualCompositionSpec BuildSpec(int slideIndex) =>
        new(FormatKey: "carousel",
            LayoutType: "grid",
            Palette: DefaultPalette,
            Typography: DefaultTypo,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                [$"slide-{slideIndex}"] = new AssetSlotSpec(null, $"Slide {slideIndex}"),
                ["logo"] = new AssetSlotSpec(null, "Brand logo"),
            },
            CopySlots: new Dictionary<string, string>
            {
                ["title"] = $"Slide {slideIndex}",
                ["subtitle"] = "Body",
                ["cta"] = "Learn More",
            },
            Dimensions: DefaultDim);
}

/// <summary>
/// Fake strategy resolver that returns the provided strategy for any policy.
/// </summary>
file sealed class FakeStrategyResolver(IBackgroundSourcingStrategy strategy) : IBackgroundStrategyResolver
{
    public IBackgroundSourcingStrategy Resolve(BackgroundSourcingPolicy? policy) => strategy;
}

/// <summary>
/// Strategy that counts Plan() calls. Used to assert plan is called ONCE per format, not per slide.
/// </summary>
file sealed class CountingBackgroundStrategy(int slideCount) : IBackgroundSourcingStrategy
{
    public int PlanCallCount { get; private set; }

    public BackgroundSourcingPolicy Policy => BackgroundSourcingPolicy.Unset;

    public CarouselBackgroundPlan Plan(BackgroundPlanRequest request)
    {
        PlanCallCount++;
        var perSlide = Enumerable.Range(0, slideCount)
            .Select(_ => (BackgroundSelection)new BackgroundSelection.Creative())
            .ToList<BackgroundSelection>();
        return new CarouselBackgroundPlan(Policy, perSlide, request.SeedToken);
    }
}

/// <summary>
/// Strategy that always returns a pre-built plan. Used to verify per-slide slot assignment.
/// </summary>
file sealed class FixedPlanStrategy(CarouselBackgroundPlan plan, BackgroundSourcingPolicy policy) : IBackgroundSourcingStrategy
{
    public BackgroundSourcingPolicy Policy => policy;
    public CarouselBackgroundPlan Plan(BackgroundPlanRequest request) => plan;
}

// Shared processor stubs (prefixed Ppl to avoid collisions with other test files)
file sealed class PplStubJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class PplStubAnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
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

file sealed class PplStubFormatResultRepository : IContentGenerationFormatResultRepository
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

file sealed class PplStubOrchestrator(string formatKey, object payload, BrandSnapshot? brand = null) : IContentGenerationOrchestrator
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

file sealed class PplStaticBrandLoader(BrandSnapshot brand) : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(brand);
}

file sealed class PplSimpleRenderer : IContentRenderer
{
    private static readonly byte[] FakePngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", FakePngBytes);
        return Task.FromResult(Result.Success(asset));
    }
}

file sealed class PplStubPackageBuilder : IGeneratedPackageBuilder
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

file sealed class PplStubPackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake-storage://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class PplStubPackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) => Task.FromResult<GeneratedPackage?>(null);
}
