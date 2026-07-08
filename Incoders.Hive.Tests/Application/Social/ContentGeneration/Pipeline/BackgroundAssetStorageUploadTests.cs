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
/// studio-background-asset-ref Slice 1, Work Unit 2 (TDD RED before implementation) — when
/// IGeneratedAssetStorage IS wired, the processor MUST upload the captured background PNG and
/// persist a storage path (not base64) as CompositionSpecEnvelope.BackgroundImageRef (BGREF-01).
///
/// Explicit regression guard: NO extra FormatAssetRow is produced for the background — this was
/// the rejected Q2 design (see design.md blast-radius correction). Only the primary slide asset
/// row(s) are upserted.
/// </summary>
public sealed class BackgroundAssetStorageUploadTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid JobId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static readonly byte[] FakeBgPngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x02];
    private static readonly string FakeBgDataUri =
        $"data:image/png;base64,{Convert.ToBase64String(FakeBgPngBytes)}";

    [Fact]
    public async Task ProcessAsync_SingleFormat_StorageWired_UploadsBackground_AndSwapsRefToStoragePath()
    {
        var assetStorage = new SpyBgAssetStorage();
        var assetRepo = new SpyBgAssetRepository();
        var formatResults = new CapturingFormatResultRepository();

        var processor = BuildProcessor(
            formatKey: "story",
            slideCount: 1,
            assetStorage: assetStorage,
            assetRepo: assetRepo,
            formatResults: formatResults);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        var envelope = DeserializeSingleEnvelope(formatResults);
        Assert.NotNull(envelope!.BackgroundImageRef);
        Assert.False(envelope.BackgroundImageRef!.StartsWith("data:", StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith("_bg.png", envelope.BackgroundImageRef);

        var bgPuts = assetStorage.Puts.Where(p => p.Key.RelativePath.EndsWith("_bg.png")).ToList();
        Assert.Single(bgPuts);
        Assert.Equal(envelope.BackgroundImageRef, bgPuts[0].ReturnedRef.StoragePath);
    }

    [Fact]
    public async Task ProcessAsync_SingleFormat_StorageWired_ProducesNoExtraFormatAssetRowForBackground()
    {
        var assetStorage = new SpyBgAssetStorage();
        var assetRepo = new SpyBgAssetRepository();
        var formatResults = new CapturingFormatResultRepository();

        var processor = BuildProcessor(
            formatKey: "story",
            slideCount: 1,
            assetStorage: assetStorage,
            assetRepo: assetRepo,
            formatResults: formatResults);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        var upsert = Assert.Single(assetRepo.UpsertCalls);
        // Exactly one row for the primary rendered asset — the background must NOT get its own row.
        Assert.Single(upsert.Assets);
        Assert.DoesNotContain(upsert.Assets, row => row.StoragePath.EndsWith("_bg.png"));
    }

    [Fact]
    public async Task ProcessAsync_Carousel_StorageWired_UploadsOneBackgroundPerSlideWithIndexedNames()
    {
        var assetStorage = new SpyBgAssetStorage();
        var assetRepo = new SpyBgAssetRepository();
        var formatResults = new CapturingFormatResultRepository();

        var processor = BuildProcessor(
            formatKey: "story-carousel",
            slideCount: 3,
            assetStorage: assetStorage,
            assetRepo: assetRepo,
            formatResults: formatResults);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        var bgNames = assetStorage.Puts
            .Where(p => p.Key.RelativePath.EndsWith("_bg.png"))
            .Select(p => p.Key.RelativePath)
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(
            ["story-carousel/slide_1_bg.png", "story-carousel/slide_2_bg.png", "story-carousel/slide_3_bg.png"],
            bgNames);

        // Regression guard: still only 3 asset rows total (one per slide's primary asset).
        var totalAssetRows = assetRepo.UpsertCalls.Sum(u => u.Assets.Count);
        Assert.Equal(3, totalAssetRows);
        Assert.DoesNotContain(
            assetRepo.UpsertCalls.SelectMany(u => u.Assets),
            row => row.StoragePath.EndsWith("_bg.png"));
    }

    [Fact]
    public async Task ProcessAsync_StorageNotWired_KeepsInlineDataUri()
    {
        var formatResults = new CapturingFormatResultRepository();

        var processor = BuildProcessor(
            formatKey: "story",
            slideCount: 1,
            assetStorage: null,
            assetRepo: null,
            formatResults: formatResults);

        await processor.ProcessAsync(JobId, CancellationToken.None);

        var envelope = DeserializeSingleEnvelope(formatResults);
        Assert.Equal(FakeBgDataUri, envelope!.BackgroundImageRef);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CompositionSpecEnvelope? DeserializeSingleEnvelope(CapturingFormatResultRepository formatResults)
    {
        var persisted = formatResults.LastUpserted;
        Assert.NotNull(persisted);
        Assert.NotNull(persisted!.CompositionSpecJson);

        var envelopes = CompositionSpecJson.ParseActiveList(persisted.CompositionSpecJson);
        return envelopes.FirstOrDefault();
    }

    private ContentGenerationJobProcessor BuildProcessor(
        string formatKey,
        int slideCount,
        IGeneratedAssetStorage? assetStorage,
        IContentGenerationFormatAssetRepository? assetRepo,
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

        var designer = new BgHtmlOutputDesigner();
        var guardrails = new BgAlwaysPassGuardrails();
        var brand = BrandSnapshot.FromBrandData(
            new BrandManualSnapshot(
                ColorPalette: [new WeightedColor("#FFFFFF", null, 0)],
                Logos: [],
                BackgroundTemplates: [],
                Typography: ["Inter"],
                VoiceAndTone: null,
                Notes: null),
            null, null);

        return new ContentGenerationJobProcessor(
            jobs: new BgStubJobRepository(fakeJob),
            analyses: new BgStubAnalysisRepository(analysisRecord),
            orchestrator: new BgStubOrchestrator(formatKey, new object(), brand),
            formatResults: formatResults,
            brandLoader: new BgStaticBrandLoader(brand),
            composer: new BgHtmlCompositionService(formatKey, slideCount),
            renderer: new BgStubGeometryRenderer(FakeBgDataUri),
            packageBuilder: new BgStubPackageBuilder(),
            packageStorage: new BgStubPackageStorage(),
            packages: new BgStubPackageRepository(),
            assetStorage: assetStorage,
            assetRepository: assetRepo,
            designer: designer,
            guardrails: guardrails,
            logger: NullLogger<ContentGenerationJobProcessor>.Instance);
    }
}

// -----------------------------------------------------------------------
// Stubs — local to this file
// -----------------------------------------------------------------------

file sealed record BgPutCall(StoredAssetKey Key, StoredAssetContent Content, StoredAssetRef ReturnedRef);
file sealed record BgUpsertCall(Guid FormatResultId, IReadOnlyList<FormatAssetRow> Assets);

file sealed class SpyBgAssetStorage : IGeneratedAssetStorage
{
    public List<BgPutCall> Puts { get; } = [];

    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content.Bytes)).ToLowerInvariant();
        var storedRef = new StoredAssetRef(
            $"{key.OrganizationId}/{key.ClientId}/{key.AnalysisId}/{key.RelativePath}",
            content.Mime,
            content.Bytes.Length,
            hash);
        Puts.Add(new BgPutCall(key, content, storedRef));
        return Task.FromResult(storedRef);
    }

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct) =>
        Task.FromResult(Result<StoredAssetStream>.Failure(StorageErrors.NotFound));

    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}

file sealed class SpyBgAssetRepository : IContentGenerationFormatAssetRepository
{
    public List<BgUpsertCall> UpsertCalls { get; } = [];

    public Task UpsertManyForFormatAsync(Guid formatResultId, IReadOnlyList<FormatAssetRow> assets, CancellationToken ct)
    {
        UpsertCalls.Add(new BgUpsertCall(formatResultId, assets));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FormatAssetRow>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormatAssetRow>>([]);

    public Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisClientAsync(Guid organizationId, Guid analysisId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormatAssetRow>>([]);

    public Task DeleteByFormatResultIdAsync(Guid formatResultId, CancellationToken ct) => Task.CompletedTask;
}

file sealed class CapturingFormatResultRepository : IContentGenerationFormatResultRepository
{
    public ContentGenerationFormatResult? LastUpserted { get; private set; }

    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult r, CancellationToken ct)
    {
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
/// Geometry-aware renderer stub that always returns the same fake background data-URI, regardless
/// of formatKey/slide, so both single-asset and multi-slide (carousel) flows capture a background.
/// </summary>
file sealed class BgStubGeometryRenderer(string backgroundDataUri) : IContentRendererWithGeometry
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
        var result = new RenderedAssetWithGeometry(asset, [], backgroundDataUri);
        return Task.FromResult(Result.Success(result));
    }
}

/// <summary>Active designer that always returns valid Html markup — triggers the Html render path.</summary>
file sealed class BgHtmlOutputDesigner : IVisualDesigner
{
    public bool IsActive => true;

    public Task<Result<VisualDesignOutput>> ComposeAsync(
        Guid organizationId,
        VisualCompositionSpec contentPayload,
        BrandManualSnapshot brandSnapshot,
        BackgroundSelection backgroundSelection,
        CancellationToken cancellationToken,
        ClientAestheticDesignContext? aesthetic = null)
    {
        const string markup = """<div class="composition" data-format="story" style="width:1080px;height:1920px;"><div class="layer-background" data-layer="background" style="background:#000000;"></div><div class="layer-content" data-layer="content"><h1 data-slot="headline" style="font-family:Inter;color:#FFFFFF;">Test</h1></div></div>""";
        return Task.FromResult(Result.Success(new VisualDesignOutput(
            Markup: markup,
            PromptVersionKey: "visual-designer/v1",
            IsValid: false)));
    }
}

file sealed class BgAlwaysPassGuardrails : IVisualGuardrailValidator
{
    public Result<VisualDesignOutput> Validate(VisualDesignOutput output, BrandManualSnapshot brand, VisualCompositionSpec spec) =>
        Result.Success(output.WithValidated());
}

/// <summary>Composer that returns N Raster specs (drives the designer+geometry-render path for both
/// single-asset formats and carousels, keyed by formatKey/slideCount from the test).</summary>
file sealed class BgHtmlCompositionService(string designedFormat, int slideCount) : IVisualCompositionService
{
    private static readonly CompositionPalette DefaultPalette = new("#333", "#666", "#999", "#FFF", "#EEE");
    private static readonly CompositionTypography DefaultTypo = new("Inter", 24, "Inter", 14);
    private static readonly CompositionDimensions DefaultDim = new(1080, 1920, 96);

    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object payload, BrandSnapshot brand, CancellationToken ct) =>
        Task.FromResult(BuildSpec(formatKey, 1));

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(
        string formatKey, object payload, BrandSnapshot brand, CancellationToken ct, CarouselBackgroundPlan? plan = null)
    {
        IReadOnlyList<VisualCompositionSpec> specs = Enumerable.Range(1, slideCount)
            .Select(i => BuildSpec(formatKey, i))
            .ToList();
        return Task.FromResult(specs);
    }

    private VisualCompositionSpec BuildSpec(string formatKey, int slideNumber)
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
            CopySlots: new Dictionary<string, string> { ["headline"] = $"Test {slideNumber}" },
            Dimensions: DefaultDim)
        with { OutputKind = outputKind, TextBody = outputKind == CompositionOutputKind.Text ? "fake body" : null };
    }

    public IReadOnlyList<string> GetInferredFields() => [];
}

file sealed class BgStubJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;
    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(job);
    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? code, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) => Task.FromResult<ContentGenerationJob?>(null);
}

file sealed class BgStubAnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
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

file sealed class BgStubOrchestrator(string formatKey, object payload, BrandSnapshot? brand = null) : IContentGenerationOrchestrator
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

file sealed class BgStaticBrandLoader(BrandSnapshot brand) : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(brand);
}

file sealed class BgStubPackageBuilder : IGeneratedPackageBuilder
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

file sealed class BgStubPackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake-storage://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class BgStubPackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) => Task.FromResult<GeneratedPackage?>(null);
}
