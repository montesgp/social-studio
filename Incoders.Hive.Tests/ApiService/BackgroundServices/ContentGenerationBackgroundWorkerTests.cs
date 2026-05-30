using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Orchestration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Shared;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.ApiService.BackgroundServices;

/// <summary>
/// Exercises the end-to-end content-generation pipeline (the ContentGenerationJobProcessor
/// in Application). The background worker itself is now a thin scope-creator that delegates
/// to this processor — its behavior is what the user actually feels when they click "Convert".
/// </summary>
public sealed class ContentGenerationBackgroundWorkerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    [Fact]
    public async Task Process_AllFormatsSucceed_BuildsPackage_StoresIt_AndMarksCompleted()
    {
        var jobId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, analysisId, status: "Queued");
        var harness = new ProcessorHarness(job, MakeAnalysisRecord(analysisId, OrgId), OrchestratorStatus.Succeeded);

        await harness.Processor.ProcessAsync(jobId, CancellationToken.None);

        Assert.Contains(("Running", (string?)null, (string?)null), harness.JobRepo.StatusUpdates);
        Assert.Contains(harness.JobRepo.StatusUpdates, t => t.status == "Completed");
        Assert.Equal("Real summary", harness.Orchestrator.LastRequest!.Analysis.Summary);
        Assert.Equal(1, harness.PackageBuilder.BuildCalls);
        Assert.Equal(1, harness.Storage.StoreCalls);
        Assert.Single(harness.Packages.Inserted);
        Assert.Single(harness.JobRepo.ManifestUpdates);
        Assert.Contains("formats", harness.JobRepo.ManifestUpdates[0]);
        Assert.NotNull(harness.PackageBuilder.LastInput?.PublicationByFormat);
        Assert.Equal(
            "carousel publication description",
            harness.PackageBuilder.LastInput!.PublicationByFormat!["carousel"].Description);
    }

    [Fact]
    public async Task Process_PreviousAiSucceededForOneFormat_RetrySkipsClaudeForThatFormat()
    {
        var jobId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, analysisId, status: "Queued",
            formats: ["carousel", "flyer"]);

        var harness = new ProcessorHarness(job, MakeAnalysisRecord(analysisId, OrgId), OrchestratorStatus.Succeeded);

        // Seed: carousel was already successfully AI-generated on a previous attempt that crashed
        // mid-render. Its OutputJson is on the format_results table.
        harness.FormatResults.Seed(new ContentGenerationFormatResult(
            Id: Guid.NewGuid(),
            JobId: jobId,
            FormatKey: "carousel",
            Status: "RenderFailed",
            OutputJson: "{\"coverTitle\":\"cached-carousel\"}",
            CompositionSpecJson: null,
            RenderedAssetUri: null,
            InferredFieldsJson: null,
            PromptVersion: "carousel.v1",
            ErrorCode: "InvalidOperationException: previous render crash",
            CreatedAt: DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt: DateTime.UtcNow.AddMinutes(-5)));

        await harness.Processor.ProcessAsync(jobId, CancellationToken.None);

        // Orchestrator must only have been asked for the missing format (flyer). Carousel's
        // cached payload is reused — no extra Claude tokens.
        Assert.NotNull(harness.Orchestrator.LastRequest);
        Assert.Equal(["flyer"], harness.Orchestrator.LastRequest!.RequestedFormats);

        // Both formats end up rendered and in the package.
        Assert.Equal(2, harness.Renderer.RenderCalls);
        Assert.Single(harness.Packages.Inserted);

        // The cached format's row is updated with the new render outcome (status Rendered), not
        // a duplicate insert — the upsert keeps a single row per (jobId, formatKey).
        var carouselFinal = harness.FormatResults.Upserts.Where(r => r.FormatKey == "carousel").Last();
        Assert.Equal("Rendered", carouselFinal.Status);
    }

    [Fact]
    public async Task Process_AnalysisMissing_FailsFast_WithoutOrchestrating()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, Guid.NewGuid(), status: "Queued");
        var harness = new ProcessorHarness(job, analysisRecord: null, OrchestratorStatus.Succeeded);

        await harness.Processor.ProcessAsync(jobId, CancellationToken.None);

        Assert.Contains(harness.JobRepo.StatusUpdates,
            t => t.status == "Failed" && t.errorCode == "analysis-not-completed");
        Assert.Null(harness.Orchestrator.LastRequest);
        Assert.Equal(0, harness.PackageBuilder.BuildCalls);
        Assert.Equal(0, harness.Storage.StoreCalls);
        Assert.Empty(harness.Packages.Inserted);
    }

    [Fact]
    public async Task Process_OrchestratorFails_NoAssets_NoPackagePersisted_AndJobFailed()
    {
        var jobId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, analysisId, status: "Queued");
        var harness = new ProcessorHarness(job, MakeAnalysisRecord(analysisId, OrgId), OrchestratorStatus.Failed);

        await harness.Processor.ProcessAsync(jobId, CancellationToken.None);

        Assert.Contains(harness.JobRepo.StatusUpdates,
            t => t.status == "Failed" && t.errorCode == "no-renderable-assets");
        Assert.Equal(0, harness.PackageBuilder.BuildCalls);
        Assert.Empty(harness.Packages.Inserted);

        // Reasons surface the actual orchestrator code, never the generic placeholder.
        var failed = harness.JobRepo.StatusUpdates.Last(s => s.status == "Failed");
        Assert.Contains("ai_failed[", failed.reason ?? "");
        Assert.Contains("fake-error", failed.reason ?? "");
    }

    [Fact]
    public async Task Process_RenderFailsForOneFormat_StillBuildsPackage_AndMarksPartial()
    {
        var jobId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, analysisId, status: "Queued",
            formats: ["carousel", "flyer"]);

        var harness = new ProcessorHarness(job, MakeAnalysisRecord(analysisId, OrgId), OrchestratorStatus.Succeeded);
        harness.Renderer.FailForFormat = "flyer";

        await harness.Processor.ProcessAsync(jobId, CancellationToken.None);

        Assert.Contains(harness.JobRepo.StatusUpdates, t => t.status == "Partial");
        Assert.Equal(1, harness.PackageBuilder.BuildCalls);
        Assert.Single(harness.Packages.Inserted);

        // The Partial status carries the actual render error in its reason — not a generic code.
        var partial = harness.JobRepo.StatusUpdates.Last(s => s.status == "Partial");
        Assert.Contains("render_failed[flyer:", partial.reason ?? "");
    }

    [Fact]
    public async Task Process_JobNotFound_NoSideEffects()
    {
        var harness = new ProcessorHarness(job: null, analysisRecord: null, OrchestratorStatus.Succeeded);

        var ex = await Record.ExceptionAsync(() =>
            harness.Processor.ProcessAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Null(ex);
        Assert.Empty(harness.JobRepo.StatusUpdates);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ContentGenerationJob MakeJob(
        Guid id, Guid orgId, Guid analysisId, string status,
        IReadOnlyList<string>? formats = null) =>
        new(
            Id: id,
            OrganizationId: orgId,
            ClientId: ClientId,
            AnalysisId: analysisId,
            RequestedFormats: formats ?? ["carousel"],
            Status: status,
            StatusReason: null,
            ErrorCode: null,
            BrandSnapshotJson: "{}",
            ManifestJson: null,
            CorrelationId: Guid.NewGuid(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            StartedAt: null,
            CompletedAt: null);

    private static SingleContentAnalysisRecord MakeAnalysisRecord(Guid id, Guid orgId)
    {
        var analysis = new SingleAnalyzerAnalysis(
            Summary: "Real summary",
            DetectedHooks: ["Hook A"],
            LikelyAudience: "Audience X",
            NarrativeStructure: "Problem → solution",
            Strengths: ["Clear opener"],
            Weaknesses: ["Slow ending"],
            DerivedIdeas: [],
            ClientAdaptationRecommendations: ["Adapt tone"],
            Confidence: 0.8,
            Warnings: []);

        var json = JsonSerializer.Serialize(analysis, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return new SingleContentAnalysisRecord(
            Id: id,
            OrganizationId: orgId,
            ClientId: ClientId,
            UserId: Guid.NewGuid(),
            SourceUrl: "https://example.com/x",
            NormalizedUrl: "https://example.com/x",
            SourcePlatformKey: "tiktok",
            Status: SingleContentAnalysisStatus.Analyzed,
            ContentSnapshotJson: "{}",
            AnalysisJson: json,
            ProviderDiagnosticsJson: null,
            FailureCode: null,
            FailureMessageKey: null,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: DateTime.UtcNow);
    }

}

// ─── Harness ──────────────────────────────────────────────────────────────────

/// <summary>Bundles every spy the processor needs so each test stays focused on assertions.</summary>
file sealed class ProcessorHarness
{
    public SpyJobRepository JobRepo { get; }
    public SpyAnalysisRepository Analyses { get; }
    public FakeOrchestrator Orchestrator { get; }
    public SpyFormatResultRepository FormatResults { get; } = new();
    public SpyBrandLoader BrandLoader { get; } = new();
    public SpyComposer Composer { get; } = new();
    public SpyRenderer Renderer { get; } = new();
    public SpyPackageBuilder PackageBuilder { get; } = new();
    public SpyStorage Storage { get; } = new();
    public SpyPackageRepository Packages { get; } = new();
    public ContentGenerationJobProcessor Processor { get; }

    public ProcessorHarness(
        ContentGenerationJob? job,
        SingleContentAnalysisRecord? analysisRecord,
        OrchestratorStatus orchestratorStatus)
    {
        JobRepo = new SpyJobRepository(job);
        Analyses = new SpyAnalysisRepository(analysisRecord);
        Orchestrator = new FakeOrchestrator(orchestratorStatus);
        Processor = new ContentGenerationJobProcessor(
            JobRepo, Analyses, Orchestrator, FormatResults, BrandLoader,
            Composer, Renderer, PackageBuilder, Storage, Packages,
            assetStorage: null, assetRepository: null,
            logger: NullLogger<ContentGenerationJobProcessor>.Instance);
    }
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

file sealed class SpyJobRepository : IContentGenerationJobRepository
{
    private readonly ContentGenerationJob? _job;
    public List<(string status, string? reason, string? errorCode)> StatusUpdates { get; } = new();
    public List<string> ManifestUpdates { get; } = new();

    public SpyJobRepository(ContentGenerationJob? job) => _job = job;

    public Task InsertAsync(ContentGenerationJob job, CancellationToken ct) => Task.CompletedTask;

    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct) =>
        Task.FromResult(_job?.Id == id && _job.OrganizationId == organizationId ? _job : null);

    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_job?.Id == id ? _job : null);

    public Task UpdateStatusAsync(Guid id, string status, string? statusReason, string? errorCode, CancellationToken ct)
    {
        StatusUpdates.Add((status, statusReason, errorCode));
        return Task.CompletedTask;
    }

    public Task UpdateManifestAsync(Guid id, string manifestJson, DateTime completedAt, CancellationToken ct)
    {
        ManifestUpdates.Add(manifestJson);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);
}

file sealed class SpyAnalysisRepository : ISingleContentAnalysisRepository
{
    private readonly SingleContentAnalysisRecord? _record;
    public SpyAnalysisRepository(SingleContentAnalysisRecord? record) => _record = record;

    public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct = default) =>
        Task.FromResult(_record?.Id == id && _record.OrganizationId == orgId ? _record : null);

    public Task AddAsync(SingleContentAnalysisRecord a, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateStatusAsync(Guid id, Guid orgId, SingleContentAnalysisStatus status, DateTime updatedAt,
        CancellationToken ct = default, string? failureCode = null, string? failureKey = null) => Task.CompletedTask;
    public Task UpdateAsync(SingleContentAnalysisRecord a, CancellationToken ct = default) => Task.CompletedTask;
    public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(
        Guid orgId, Guid userId, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));
    public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(
        Guid orgId, Guid userId, string normalizedUrl, string sourceUrl, CancellationToken ct = default) =>
        Task.FromResult<SingleContentAnalysisHistoryItem?>(null);
    public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(
        Guid id, Guid orgId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>?>(null);
}

file sealed class FakeOrchestrator : IContentGenerationOrchestrator
{
    private readonly OrchestratorStatus _status;
    public OrchestratorRequest? LastRequest { get; private set; }

    public FakeOrchestrator(OrchestratorStatus status) => _status = status;

    public Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var perFormat = new Dictionary<string, FormatGenerationResult<object>>();
        foreach (var fmt in request.RequestedFormats)
        {
            perFormat[fmt] = _status switch
            {
                OrchestratorStatus.Succeeded or OrchestratorStatus.Partial =>
                    FormatGenerationResult<object>.Succeeded(
                        fmt switch
                        {
                            "carousel" => new { format = fmt, publicationDescription = "carousel publication description" },
                            "flyer" => new { format = fmt, publicationDescription = "flyer publication description" },
                            _ => new { format = fmt }
                        },
                        "v1",
                        []),
                _ => FormatGenerationResult<object>.Failed("v1", "fake-error"),
            };
        }
        return Task.FromResult(new OrchestratorResult(_status, perFormat, BrandSnapshot.Empty));
    }
}

file sealed class SpyFormatResultRepository : IContentGenerationFormatResultRepository
{
    private readonly Dictionary<(Guid, string), ContentGenerationFormatResult> _store = new();
    public List<ContentGenerationFormatResult> Upserts { get; } = new();

    public void Seed(ContentGenerationFormatResult r)
    {
        _store[(r.JobId, r.FormatKey)] = r;
    }

    public Task UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct)
    {
        Upserts.Add(result);
        _store[(result.JobId, result.FormatKey)] = result;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct)
    {
        var rows = _store.Values.Where(r => r.JobId == jobId).ToList();
        return Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>(rows);
    }
}

file sealed class SpyBrandLoader : IClientBrandSnapshotLoader
{
    public int LoadCalls { get; private set; }

    public Task<BrandSnapshot> LoadAsync(Guid organizationId, Guid clientId, CancellationToken cancellationToken = default)
    {
        LoadCalls++;
        return Task.FromResult(BrandSnapshot.Empty);
    }
}

file sealed class SpyComposer : IVisualCompositionService
{
    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object generatedPayload, BrandSnapshot brand, CancellationToken ct) =>
        Task.FromResult(new VisualCompositionSpec(
            FormatKey: formatKey,
            LayoutType: "stack",
            Palette: new CompositionPalette("#000000", "#111111", "#222222", "#333333", "#FFFFFF"),
            Typography: new CompositionTypography("Inter", 32, "Inter", 16),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["title"] = "stub" },
            Dimensions: new CompositionDimensions(100, 100, 72)));

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(string formatKey, object generatedPayload, BrandSnapshot brand, CancellationToken ct)
    {
        var spec = new VisualCompositionSpec(
            FormatKey: formatKey,
            LayoutType: "stack",
            Palette: new CompositionPalette("#000000", "#111111", "#222222", "#333333", "#FFFFFF"),
            Typography: new CompositionTypography("Inter", 32, "Inter", 16),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["title"] = "stub" },
            Dimensions: new CompositionDimensions(100, 100, 72));
        return Task.FromResult<IReadOnlyList<VisualCompositionSpec>>([spec]);
    }

    public IReadOnlyList<string> GetInferredFields() => [];
}

file sealed class SpyRenderer : IContentRenderer
{
    public int RenderCalls { get; private set; }
    public string? FailForFormat { get; set; }

    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        RenderCalls++;
        if (FailForFormat is not null && string.Equals(spec.FormatKey, FailForFormat, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Result.Failure<RenderedAsset>(
                Error.Failure("render.failure", "simulated")));
        }
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", [0x89]);
        return Task.FromResult(Result.Success(asset));
    }
}

file sealed class SpyPackageBuilder : IGeneratedPackageBuilder
{
    public int BuildCalls { get; private set; }
    public BuildPackageInput? LastInput { get; private set; }

    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        BuildCalls++;
        LastInput = input;
        var ms = new MemoryStream([0x50, 0x4B]);
        var manifest = new GeneratedPackageManifest(
            Formats: input.Assets.ToDictionary(
                a => a.FormatKey,
                a => new FormatAssetMetadata(a.FormatKey, a.Mime, a.Filename, a.SizeBytes, a.ContentHash)),
            InferredFields: input.InferredFields.ToList(),
            Warnings: input.Warnings.ToList(),
            TotalSizeBytes: ms.Length);
        return Task.FromResult(new BuiltPackage(ms, ms.Length, manifest, $"{input.JobId:N}.zip"));
    }
}

file sealed class SpyStorage : IGeneratedPackageStorage
{
    public int StoreCalls { get; private set; }
    public List<StorePackageKey> Keys { get; } = new();

    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct)
    {
        StoreCalls++;
        Keys.Add(key);
        return Task.FromResult(new StoredPackageRef($"file://{key.OrganizationId:N}/{key.JobId:N}.zip"));
    }

    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.CompletedTask;
}

file sealed class SpyPackageRepository : IGeneratedPackageRepository
{
    public List<GeneratedPackage> Inserted { get; } = new();

    public Task InsertAsync(GeneratedPackage package, CancellationToken ct)
    {
        Inserted.Add(package);
        return Task.CompletedTask;
    }

    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) =>
        Task.FromResult<GeneratedPackage?>(null);
}
