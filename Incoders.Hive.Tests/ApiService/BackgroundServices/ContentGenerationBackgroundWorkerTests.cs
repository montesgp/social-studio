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
///
/// The previous bug: the worker passed an empty <see cref="SingleAnalyzerAnalysis"/> stub to
/// the orchestrator and never built/stored a package — so jobs always ended "Failed" and no
/// ZIP was ever produced. These tests pin down the fixed flow.
/// </summary>
public sealed class ContentGenerationBackgroundWorkerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public async Task Process_AllFormatsSucceed_BuildsPackage_StoresIt_AndMarksCompleted()
    {
        var jobId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, analysisId, status: "Queued");

        var jobRepo = new SpyJobRepository(job);
        var analyses = new SpyAnalysisRepository(MakeAnalysisRecord(analysisId, OrgId));
        var orchestrator = new FakeOrchestrator(OrchestratorStatus.Succeeded);
        var composer = new SpyComposer();
        var renderer = new SpyRenderer();
        var packageBuilder = new SpyPackageBuilder();
        var storage = new SpyStorage();
        var packages = new SpyPackageRepository();

        var processor = new ContentGenerationJobProcessor(
            jobRepo, analyses, orchestrator, composer, renderer, packageBuilder, storage, packages,
            NullLogger<ContentGenerationJobProcessor>.Instance);

        await processor.ProcessAsync(jobId, CancellationToken.None);

        // Running first, then Completed
        Assert.Contains(("Running", (string?)null, (string?)null), jobRepo.StatusUpdates);
        Assert.Contains(("Completed", (string?)null, (string?)null), jobRepo.StatusUpdates);

        // The orchestrator received the REAL analysis (not the previous empty stub).
        Assert.Equal("Real summary", orchestrator.LastRequest!.Analysis.Summary);

        // The package was actually built and stored — this is the regression that broke before.
        Assert.Equal(1, packageBuilder.BuildCalls);
        Assert.Equal(1, storage.StoreCalls);
        Assert.Single(packages.Inserted);
        Assert.Equal(jobId, packages.Inserted[0].JobId);
        Assert.False(string.IsNullOrEmpty(packages.Inserted[0].StorageUri));

        // The manifest persisted on the job is the FULL builder output (with formats), not the
        // reduced placeholder we used to write.
        Assert.Single(jobRepo.ManifestUpdates);
        Assert.Contains("formats", jobRepo.ManifestUpdates[0]);
    }

    [Fact]
    public async Task Process_AnalysisMissing_FailsFast_WithoutOrchestrating()
    {
        var jobId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, Guid.NewGuid(), status: "Queued");

        var jobRepo = new SpyJobRepository(job);
        var analyses = new SpyAnalysisRepository(record: null);
        var orchestrator = new FakeOrchestrator(OrchestratorStatus.Succeeded);
        var composer = new SpyComposer();
        var renderer = new SpyRenderer();
        var packageBuilder = new SpyPackageBuilder();
        var storage = new SpyStorage();
        var packages = new SpyPackageRepository();

        var processor = new ContentGenerationJobProcessor(
            jobRepo, analyses, orchestrator, composer, renderer, packageBuilder, storage, packages);

        await processor.ProcessAsync(jobId, CancellationToken.None);

        // Job is marked Failed with a CODE that points at the real problem — not "unexpected-error".
        Assert.Contains(jobRepo.StatusUpdates,
            t => t.status == "Failed" && t.errorCode == "analysis-not-completed");

        // No work was done downstream — the regression we're guarding against was the worker
        // pretending an empty analysis was valid input.
        Assert.Null(orchestrator.LastRequest);
        Assert.Equal(0, packageBuilder.BuildCalls);
        Assert.Equal(0, storage.StoreCalls);
        Assert.Empty(packages.Inserted);
    }

    [Fact]
    public async Task Process_OrchestratorFails_NoAssets_NoPackagePersisted_AndJobFailed()
    {
        var jobId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, analysisId, status: "Queued");

        var jobRepo = new SpyJobRepository(job);
        var analyses = new SpyAnalysisRepository(MakeAnalysisRecord(analysisId, OrgId));
        var orchestrator = new FakeOrchestrator(OrchestratorStatus.Failed);
        var composer = new SpyComposer();
        var renderer = new SpyRenderer();
        var packageBuilder = new SpyPackageBuilder();
        var storage = new SpyStorage();
        var packages = new SpyPackageRepository();

        var processor = new ContentGenerationJobProcessor(
            jobRepo, analyses, orchestrator, composer, renderer, packageBuilder, storage, packages);

        await processor.ProcessAsync(jobId, CancellationToken.None);

        Assert.Contains(jobRepo.StatusUpdates,
            t => t.status == "Failed" && t.errorCode == "no-renderable-assets");
        Assert.Equal(0, packageBuilder.BuildCalls);
        Assert.Equal(0, storage.StoreCalls);
        Assert.Empty(packages.Inserted);
    }

    [Fact]
    public async Task Process_RenderFailsForOneFormat_StillBuildsPackage_AndMarksPartial()
    {
        var jobId = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var job = MakeJob(jobId, OrgId, analysisId, status: "Queued",
            formats: ["carousel", "flyer"]);

        var jobRepo = new SpyJobRepository(job);
        var analyses = new SpyAnalysisRepository(MakeAnalysisRecord(analysisId, OrgId));
        var orchestrator = new FakeOrchestrator(OrchestratorStatus.Succeeded);
        var composer = new SpyComposer();
        var renderer = new SpyRenderer { FailForFormat = "flyer" };
        var packageBuilder = new SpyPackageBuilder();
        var storage = new SpyStorage();
        var packages = new SpyPackageRepository();

        var processor = new ContentGenerationJobProcessor(
            jobRepo, analyses, orchestrator, composer, renderer, packageBuilder, storage, packages);

        await processor.ProcessAsync(jobId, CancellationToken.None);

        // Carousel rendered → package is built and stored, but final status is Partial because
        // one format failed to render. The user gets a downloadable ZIP that's just incomplete.
        Assert.Contains(jobRepo.StatusUpdates, t => t.status == "Partial");
        Assert.Equal(1, packageBuilder.BuildCalls);
        Assert.Single(packages.Inserted);
    }

    [Fact]
    public async Task Process_JobNotFound_NoSideEffects()
    {
        var jobRepo = new SpyJobRepository(null);
        var analyses = new SpyAnalysisRepository(record: null);
        var orchestrator = new FakeOrchestrator(OrchestratorStatus.Succeeded);
        var composer = new SpyComposer();
        var renderer = new SpyRenderer();
        var packageBuilder = new SpyPackageBuilder();
        var storage = new SpyStorage();
        var packages = new SpyPackageRepository();

        var processor = new ContentGenerationJobProcessor(
            jobRepo, analyses, orchestrator, composer, renderer, packageBuilder, storage, packages);

        var ex = await Record.ExceptionAsync(() =>
            processor.ProcessAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Null(ex);
        Assert.Empty(jobRepo.StatusUpdates);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ContentGenerationJob MakeJob(
        Guid id, Guid orgId, Guid analysisId, string status,
        IReadOnlyList<string>? formats = null) =>
        new(
            Id: id,
            OrganizationId: orgId,
            ClientId: null,
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
        // Same JsonSerializerDefaults.Web shape the analyzer writes to the DB.
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
            ClientId: null,
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
                    FormatGenerationResult<object>.Succeeded(new object(), "v1", []),
                _ => FormatGenerationResult<object>.Failed("v1", "fake-error"),
            };
        }
        return Task.FromResult(new OrchestratorResult(_status, perFormat, BrandSnapshot.Empty));
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

    public IReadOnlyList<string> GetInferredFields() => [];
}

file sealed class SpyRenderer : IContentRenderer
{
    public string? FailForFormat { get; set; }

    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        if (FailForFormat is not null && string.Equals(spec.FormatKey, FailForFormat, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Result.Failure<RenderedAsset>(
                Error.Failure("render.failure", "simulated")));
        }
        // Single non-empty byte so RenderedAsset.Create accepts it.
        var asset = RenderedAsset.Create(spec.FormatKey, "image/png", $"{spec.FormatKey}.png", [0x89]);
        return Task.FromResult(Result.Success(asset));
    }
}

file sealed class SpyPackageBuilder : IGeneratedPackageBuilder
{
    public int BuildCalls { get; private set; }

    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        BuildCalls++;
        var ms = new MemoryStream([0x50, 0x4B]); // "PK" — anything non-empty
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
