using System.Text.Json;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Carousel;
using SocialStudio.Application.Features.Social.ContentGeneration.Formats.Story;
using SocialStudio.Application.Features.Social.ContentGeneration.Orchestration;
using SocialStudio.Application.Features.Social.ContentGeneration.Pipeline;
using SocialStudio.Application.Features.Social.ContentGeneration.Shared;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ClientBrandManuals;
using SocialStudio.Domain.Social.ContentGeneration;
using SocialStudio.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Pipeline;

/// <summary>
/// WU-7 TDD tests — concision violation advisory logging (CG-CON-03).
///
/// These tests assert that when a format generator returns copy that exceeds a slot's
/// advisory soft limit, the processor emits a structured LogWarning and continues without
/// error, retry, or failure path change.
///
/// Written RED before adding CheckConcisionAdvisory to ContentGenerationJobProcessor.
/// Turned GREEN by adding the lightweight post-copy check after format payload is confirmed.
/// </summary>
public sealed class ConcisionViolationLoggingTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaa0-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbb0-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AnalysisId = Guid.Parse("ccccccc0-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid JobId = Guid.Parse("ddddddd0-dddd-dddd-dddd-dddddddddddd");

    // ─── Story: oversized title should log warning ────────────────────────────

    /// <summary>
    /// CG-CON-03: When the story title exceeds the advisory bound, a LogWarning is emitted
    /// with {jobId, formatKey, slotName, advisoryBound, actualLength}.
    /// The pipeline must still complete successfully (no exception, no retry).
    /// </summary>
    [Fact]
    public async Task ProcessAsync_StoryWithOversizedTitle_LogsAdvisoryWarning()
    {
        // An oversized title — well beyond the advisory ~80 char headline limit
        var longTitle = new string('A', 200);
        var storyPayload = new
        {
            title = longTitle,
            subtitle = (string?)null,
            body = (string?)null,
            callouts = Array.Empty<string>(),
            ctaText = "Swipe up",
            backgroundPrompt = (string?)null,
            signatureText = (string?)null,
            layoutHint = (string?)null
        };

        var capturingLogger = new CapturingLogger<ContentGenerationJobProcessor>();
        var processor = BuildProcessor("story-single", storyPayload, capturingLogger);

        // Act — should complete without throwing
        await processor.ProcessAsync(JobId, CancellationToken.None);

        // Assert — a warning was logged with the violation metadata
        var warnings = capturingLogger.Warnings;
        Assert.NotEmpty(warnings);

        var violationWarning = warnings.FirstOrDefault(w =>
            w.Contains("concision") && w.Contains("story-single"));
        Assert.NotNull(violationWarning);

        // Must reference the slot and lengths
        Assert.Contains("title", violationWarning, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CG-CON-03: Processing continues and succeeds even when a concision violation is detected.
    /// The advisory log must not raise an exception or change the result outcome.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_StoryWithOversizedTitle_PipelineContinues()
    {
        var longTitle = new string('X', 200);
        var storyPayload = new
        {
            title = longTitle,
            subtitle = (string?)null,
            body = (string?)null,
            callouts = Array.Empty<string>(),
            ctaText = "Go",
            backgroundPrompt = (string?)null,
            signatureText = (string?)null,
            layoutHint = (string?)null
        };

        var capturingLogger = new CapturingLogger<ContentGenerationJobProcessor>();
        var processor = BuildProcessor("story-single", storyPayload, capturingLogger);

        // Must not throw even on advisory violation
        var exception = await Record.ExceptionAsync(
            () => processor.ProcessAsync(JobId, CancellationToken.None));

        Assert.Null(exception);
    }

    // ─── Story: within-limit title should NOT log a violation warning ─────────

    /// <summary>
    /// CG-CON-03: When copy is within advisory limits, no concision warning is emitted.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_StoryWithShortTitle_NoAdvisoryWarning()
    {
        var storyPayload = new
        {
            title = "Short title",   // well within any advisory bound
            subtitle = (string?)null,
            body = (string?)null,
            callouts = Array.Empty<string>(),
            ctaText = "Go",
            backgroundPrompt = (string?)null,
            signatureText = (string?)null,
            layoutHint = (string?)null
        };

        var capturingLogger = new CapturingLogger<ContentGenerationJobProcessor>();
        var processor = BuildProcessor("story-single", storyPayload, capturingLogger);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        var concisionWarnings = capturingLogger.Warnings.Where(w => w.Contains("concision")).ToList();
        Assert.Empty(concisionWarnings);
    }

    // ─── Carousel: oversized slide body should log warning ────────────────────

    /// <summary>
    /// CG-CON-03 (carousel): oversized slide body triggers advisory warning for the carousel format.
    /// </summary>
    [Fact]
    public async Task ProcessAsync_CarouselWithOversizedSlideBody_LogsAdvisoryWarning()
    {
        var longBody = new string('B', 300);
        var carouselPayload = new
        {
            coverTitle = "Short cover",
            slides = new[]
            {
                new { title = "Slide 1", body = longBody, imagePromptHint = (string?)null }
            },
            ctaText = "Follow",
            ctaLink = (string?)null
        };

        var capturingLogger = new CapturingLogger<ContentGenerationJobProcessor>();
        var processor = BuildProcessor("carousel", carouselPayload, capturingLogger);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        var warnings = capturingLogger.Warnings;
        var violationWarning = warnings.FirstOrDefault(w =>
            w.Contains("concision") && w.Contains("carousel"));
        Assert.NotNull(violationWarning);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static BrandSnapshot BuildBrand()
    {
        var brandManual = new BrandManualSnapshot(
            ColorPalette: Array.Empty<WeightedColor>(),
            Logos: Array.Empty<string>(),
            BackgroundTemplates: Array.Empty<BrandBackgroundTemplateSnapshot>(),
            Typography: Array.Empty<string>(),
            VoiceAndTone: null,
            Notes: null);
        return BrandSnapshot.FromBrandData(brandManual, null, null);
    }

    private static ContentGenerationJobProcessor BuildProcessor(
        string formatKey,
        object payload,
        ILogger<ContentGenerationJobProcessor> logger)
    {
        var brand = BuildBrand();

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

        return new ContentGenerationJobProcessor(
            jobs: new CvlStubJobRepository(fakeJob),
            analyses: new CvlStubAnalysisRepository(analysisRecord),
            orchestrator: new CvlStubOrchestrator(formatKey, payload, brand),
            formatResults: new CvlStubFormatResultRepository(),
            brandLoader: new CvlStaticBrandLoader(brand),
            composer: new CvlSimpleComposer(),
            renderer: new CvlSimpleRenderer(),
            packageBuilder: new CvlStubPackageBuilder(),
            packageStorage: new CvlStubPackageStorage(),
            packages: new CvlStubPackageRepository(),
            assetStorage: null,
            assetRepository: null,
            designer: null,
            guardrails: null,
            logger: logger,
            backgroundStrategyResolver: null);
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

// ─── Processor stubs (Cvl prefix to avoid collisions) ────────────────────────

file sealed class CvlStubJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class CvlStubAnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
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

file sealed class CvlStubFormatResultRepository : IContentGenerationFormatResultRepository
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

file sealed class CvlStubOrchestrator(string formatKey, object payload, BrandSnapshot? brand = null) : IContentGenerationOrchestrator
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

file sealed class CvlStaticBrandLoader(BrandSnapshot brand) : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(brand);
}

file sealed class CvlSimpleComposer : IVisualCompositionService
{
    private static readonly CompositionPalette Palette = new("#111", "#222", "#333", "#FFF", "#EEE");
    private static readonly CompositionTypography Typo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions Dim = new(1080, 1080, 96);

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, ClientAestheticProfile? aestheticProfile = null) =>
        Task.FromResult(BuildSpec(formatKey, 1));

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey,
        object payload,
        BrandSnapshot brand,
        CancellationToken ct,
        CarouselBackgroundPlan? plan = null, ClientAestheticProfile? aestheticProfile = null)
    {
        var specs = new List<VisualCompositionSpec> { BuildSpec(formatKey, 1) };
        return Task.FromResult<IReadOnlyList<VisualCompositionSpec>>(specs);
    }

    public IReadOnlyList<string> GetInferredFields() => [];

    private static VisualCompositionSpec BuildSpec(string formatKey, int index) =>
        new(FormatKey: formatKey,
            LayoutType: "hero",
            Palette: Palette,
            Typography: Typo,
            AssetSlots: new Dictionary<string, AssetSlotSpec>
            {
                [$"slide-{index}"] = new AssetSlotSpec(null, "bg"),
            },
            CopySlots: new Dictionary<string, string>
            {
                ["title"] = "Title",
                ["cta"] = "CTA",
            },
            Dimensions: Dim);
}

file sealed class CvlSimpleRenderer : IContentRenderer
{
    private static readonly byte[] FakePng = [0x89, 0x50, 0x4E, 0x47];

    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", FakePng);
        return Task.FromResult(Result.Success(asset));
    }
}

file sealed class CvlStubPackageBuilder : IGeneratedPackageBuilder
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

file sealed class CvlStubPackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake-storage://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class CvlStubPackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) => Task.FromResult<GeneratedPackage?>(null);
}
