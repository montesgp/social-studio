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
/// Change 1 (incident job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1): a job that generates several
/// carousel slides successfully and is THEN cancelled (worker wall-clock timeout / host shutdown)
/// mid-render must not discard the slides that already finished. These tests drive the processor
/// against a renderer that cancels the job's own token partway through a 3-slide carousel and assert
/// that the already-rendered slides are durably checkpointed — not silently dropped by the old
/// batch-at-the-end-of-format persistence.
/// </summary>
public sealed class PartialPersistenceOnCancellationTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid AnalysisId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();

    [Fact]
    public async Task ProcessAsync_CancelledMidCarousel_PersistsAssetsForSlidesThatAlreadyFinished()
    {
        var assetStorage = new PpAssetStorage();
        var assetRepo = new PpAssetRepository();
        var formatResults = new PpFormatResultRepository();
        var jobRepo = new PpJobRepository(BuildJob());
        using var cts = new CancellationTokenSource();

        var processor = BuildProcessor(
            jobRepo, formatResults, assetRepo, assetStorage,
            renderer: new PpCancelOnThirdSlideRenderer(cts));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.ProcessAsync(JobId, cts.Token));

        // Slides 1 and 2 rendered and uploaded successfully before slide 3 triggered cancellation.
        // UpsertManyForFormatAsync's contract is "replace all rows for this format result" so the
        // LAST call reflects the durable end state.
        Assert.NotEmpty(assetRepo.UpsertCalls);
        var finalRows = assetRepo.UpsertCalls[^1].Assets;
        Assert.Equal(2, finalRows.Count);
        Assert.Contains(finalRows, r => r.AssetIndex == 1);
        Assert.Contains(finalRows, r => r.AssetIndex == 2);
        Assert.DoesNotContain(finalRows, r => r.AssetIndex == 3);
    }

    [Fact]
    public async Task ProcessAsync_CancelledMidCarousel_FormatResultCheckpointsAsPartiallyRendered()
    {
        var assetStorage = new PpAssetStorage();
        var assetRepo = new PpAssetRepository();
        var formatResults = new PpFormatResultRepository();
        var jobRepo = new PpJobRepository(BuildJob());
        using var cts = new CancellationTokenSource();

        var processor = BuildProcessor(
            jobRepo, formatResults, assetRepo, assetStorage,
            renderer: new PpCancelOnThirdSlideRenderer(cts));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.ProcessAsync(JobId, cts.Token));

        // The AI stage persists an "AiSucceeded" checkpoint under the same (JobId, FormatKey) row
        // before rendering starts; the per-slide render checkpoint upserts the SAME row (by id), so
        // there is still exactly one row for this format, now advanced to "PartiallyRendered".
        var persisted = Assert.Single(formatResults.AllForJob(JobId));
        Assert.Equal("PartiallyRendered", persisted.Status);
    }

    [Fact]
    public async Task ProcessAsync_CancelledAfterPersistingWork_JobStatusIsPartialNotFailed()
    {
        var assetStorage = new PpAssetStorage();
        var assetRepo = new PpAssetRepository();
        var formatResults = new PpFormatResultRepository();
        var jobRepo = new PpJobRepository(BuildJob());
        using var cts = new CancellationTokenSource();

        var processor = BuildProcessor(
            jobRepo, formatResults, assetRepo, assetStorage,
            renderer: new PpCancelOnThirdSlideRenderer(cts));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.ProcessAsync(JobId, cts.Token));

        // Reuses the existing "Partial" job status (already used for some-formats-failed jobs) rather
        // than "Failed" — a cancelled job that safeguarded real output must not read identically to
        // one that produced nothing.
        var finalStatusCall = Assert.Single(jobRepo.StatusCalls, c => c.Status is "Partial" or "Failed");
        Assert.Equal("Partial", finalStatusCall.Status);
        Assert.Equal("cancelled-partial", finalStatusCall.ErrorCode);
    }

    [Fact]
    public async Task ProcessAsync_BudgetTimeoutContextConfirmed_RecordsPreciseTimeoutReasonAndCode()
    {
        // Gap 1 (incident job 8757e85a-6acd-49b4-af11-a2d19fbcd3e1, Change 5): when the caller
        // (the background worker) supplies a cancellation context whose host token is NOT cancelled,
        // only the job's own wall-clock budget could have fired — the terminal write must say so
        // precisely, distinct from a plain "cancelled".
        var assetStorage = new PpAssetStorage();
        var assetRepo = new PpAssetRepository();
        var formatResults = new PpFormatResultRepository();
        var jobRepo = new PpJobRepository(BuildJob());
        using var cts = new CancellationTokenSource();
        using var hostShutdownCts = new CancellationTokenSource(); // never cancelled — host is alive

        var processor = BuildProcessor(
            jobRepo, formatResults, assetRepo, assetStorage,
            renderer: new PpCancelOnFirstSlideRenderer(cts));

        var budgetLimit = TimeSpan.FromMinutes(60);
        var cancellationContext = new ContentGenerationJobCancellationContext(hostShutdownCts.Token, budgetLimit);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.ProcessAsync(JobId, cts.Token, cancellationContext));

        var finalStatusCall = Assert.Single(jobRepo.StatusCalls, c => c.Status is "Partial" or "Failed");
        Assert.Equal("Failed", finalStatusCall.Status);
        Assert.Equal("cancelled-timeout", finalStatusCall.ErrorCode);
        Assert.Contains("60000", finalStatusCall.Reason); // configured limit, in ms
        Assert.Contains("job exceeded the configured wall-clock limit", finalStatusCall.Reason);
    }

    [Fact]
    public async Task ProcessAsync_HostShutdownContextConfirmed_KeepsGenericCancelledClassification()
    {
        // Control case: the host token IS cancelled at the moment ct is — this is a host shutdown
        // (or any cancellation that is not confirmed as the job's own budget), not a budget timeout,
        // so the pre-existing generic "cancelled" classification must be kept, not misreported as a
        // timeout.
        var assetStorage = new PpAssetStorage();
        var assetRepo = new PpAssetRepository();
        var formatResults = new PpFormatResultRepository();
        var jobRepo = new PpJobRepository(BuildJob());
        using var cts = new CancellationTokenSource();
        using var hostShutdownCts = new CancellationTokenSource();
        hostShutdownCts.Cancel();

        var processor = BuildProcessor(
            jobRepo, formatResults, assetRepo, assetStorage,
            renderer: new PpCancelOnFirstSlideRenderer(cts));

        var cancellationContext = new ContentGenerationJobCancellationContext(hostShutdownCts.Token, TimeSpan.FromMinutes(60));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.ProcessAsync(JobId, cts.Token, cancellationContext));

        var finalStatusCall = Assert.Single(jobRepo.StatusCalls, c => c.Status is "Partial" or "Failed");
        Assert.Equal("Failed", finalStatusCall.Status);
        Assert.Equal("cancelled", finalStatusCall.ErrorCode);
    }

    [Fact]
    public async Task ProcessAsync_BudgetTimeoutWithPersistedWork_RecordsPartialTimeoutCode()
    {
        var assetStorage = new PpAssetStorage();
        var assetRepo = new PpAssetRepository();
        var formatResults = new PpFormatResultRepository();
        var jobRepo = new PpJobRepository(BuildJob());
        using var cts = new CancellationTokenSource();
        using var hostShutdownCts = new CancellationTokenSource();

        var processor = BuildProcessor(
            jobRepo, formatResults, assetRepo, assetStorage,
            renderer: new PpCancelOnThirdSlideRenderer(cts));

        var cancellationContext = new ContentGenerationJobCancellationContext(hostShutdownCts.Token, TimeSpan.FromMinutes(60));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.ProcessAsync(JobId, cts.Token, cancellationContext));

        var finalStatusCall = Assert.Single(jobRepo.StatusCalls, c => c.Status is "Partial" or "Failed");
        Assert.Equal("Partial", finalStatusCall.Status);
        Assert.Equal("cancelled-timeout-partial", finalStatusCall.ErrorCode);
        Assert.Contains("some format(s) already persisted assets", finalStatusCall.Reason);
    }

    [Fact]
    public async Task ProcessAsync_CancelledBeforeAnySlideFinished_JobStatusStaysFailed()
    {
        // Control case: cancellation on the FIRST slide, before anything checkpointed — must keep the
        // pre-existing "Failed"/"cancelled" behavior, not report a false partial success.
        var assetStorage = new PpAssetStorage();
        var assetRepo = new PpAssetRepository();
        var formatResults = new PpFormatResultRepository();
        var jobRepo = new PpJobRepository(BuildJob());
        using var cts = new CancellationTokenSource();

        var processor = BuildProcessor(
            jobRepo, formatResults, assetRepo, assetStorage,
            renderer: new PpCancelOnFirstSlideRenderer(cts));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => processor.ProcessAsync(JobId, cts.Token));

        // The AI stage still persists its own "AiSucceeded" checkpoint (unrelated to Change 1 — that
        // happens before any rendering starts) but no render checkpoint (PartiallyRendered/Rendered)
        // ever fires, so HasAnyPersistedFormatResultAsync correctly reports nothing was safeguarded.
        Assert.DoesNotContain(
            formatResults.AllForJob(JobId),
            r => r.Status is "PartiallyRendered" or "Rendered");
        var finalStatusCall = Assert.Single(jobRepo.StatusCalls, c => c.Status is "Partial" or "Failed");
        Assert.Equal("Failed", finalStatusCall.Status);
        Assert.Equal("cancelled", finalStatusCall.ErrorCode);
    }

    // -----------------------------------------------------------------------
    // Fixture
    // -----------------------------------------------------------------------

    private static ContentGenerationJob BuildJob() => new(
        Id: JobId,
        OrganizationId: OrgId,
        ClientId: ClientId,
        AnalysisId: AnalysisId,
        RequestedFormats: ["story-carousel"],
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

    private static ContentGenerationJobProcessor BuildProcessor(
        IContentGenerationJobRepository jobRepo,
        IContentGenerationFormatResultRepository formatResults,
        IContentGenerationFormatAssetRepository assetRepo,
        IGeneratedAssetStorage assetStorage,
        IContentRenderer renderer)
    {
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
            jobs: jobRepo,
            analyses: new PpAnalysisRepository(analysisRecord),
            orchestrator: new PpOrchestrator("story-carousel", new object(), brand),
            formatResults: formatResults,
            brandLoader: new PpBrandLoader(brand),
            composer: new PpCompositionService("story-carousel", slideCount: 3),
            renderer: renderer,
            packageBuilder: new PpPackageBuilder(),
            packageStorage: new PpPackageStorage(),
            packages: new PpPackageRepository(),
            assetStorage: assetStorage,
            assetRepository: assetRepo,
            designer: null,
            guardrails: null,
            logger: NullLogger<ContentGenerationJobProcessor>.Instance);
    }
}

// -----------------------------------------------------------------------
// Stubs — local to this file (prefix "Pp" = PartialPersistence)
// -----------------------------------------------------------------------

file sealed record PpStatusCall(Guid JobId, string Status, string? Reason, string? ErrorCode);

file sealed class PpJobRepository(ContentGenerationJob job) : IContentGenerationJobRepository
{
    private ContentGenerationJob _job = job;

    public List<PpStatusCall> StatusCalls { get; } = [];

    public Task InsertAsync(ContentGenerationJob j, CancellationToken ct) => Task.CompletedTask;

    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid orgId, CancellationToken ct) =>
        Task.FromResult<ContentGenerationJob?>(_job);

    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<ContentGenerationJob?>(_job);

    public Task UpdateStatusAsync(Guid id, string status, string? reason, string? errorCode, CancellationToken ct)
    {
        StatusCalls.Add(new PpStatusCall(id, status, reason, errorCode));
        _job = _job with { Status = status, StatusReason = reason, ErrorCode = errorCode };
        return Task.CompletedTask;
    }

    public Task UpdateManifestAsync(Guid id, string json, DateTime completedAt, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);

    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(
        Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) =>
        Task.FromResult<ContentGenerationJob?>(null);
}

/// <summary>In-memory upsert-by-(JobId,FormatKey) fake mirroring the real EF repository's contract
/// closely enough to observe checkpoint status transitions.</summary>
file sealed class PpFormatResultRepository : IContentGenerationFormatResultRepository
{
    private readonly Dictionary<(Guid JobId, string FormatKey), ContentGenerationFormatResult> _byKey = new();

    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct)
    {
        var key = (result.JobId, result.FormatKey);
        var stored = _byKey.TryGetValue(key, out var existing)
            ? result with { Id = existing.Id }
            : result;
        _byKey[key] = stored;
        return Task.FromResult(stored);
    }

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>(
            _byKey.Values.Where(r => r.JobId == jobId).ToList());

    public IReadOnlyList<ContentGenerationFormatResult> AllForJob(Guid jobId) =>
        _byKey.Values.Where(r => r.JobId == jobId).ToList();
}

file sealed record PpUpsertCall(Guid FormatResultId, IReadOnlyList<FormatAssetRow> Assets);

file sealed class PpAssetRepository : IContentGenerationFormatAssetRepository
{
    public List<PpUpsertCall> UpsertCalls { get; } = [];

    public Task UpsertManyForFormatAsync(Guid formatResultId, IReadOnlyList<FormatAssetRow> assets, CancellationToken ct)
    {
        UpsertCalls.Add(new PpUpsertCall(formatResultId, assets));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FormatAssetRow>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormatAssetRow>>([]);

    public Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisClientAsync(Guid organizationId, Guid analysisId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormatAssetRow>>([]);

    public Task DeleteByFormatResultIdAsync(Guid formatResultId, CancellationToken ct) => Task.CompletedTask;
}

file sealed class PpAssetStorage : IGeneratedAssetStorage
{
    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content.Bytes)).ToLowerInvariant();
        return Task.FromResult(new StoredAssetRef(
            $"{key.OrganizationId}/{key.ClientId}/{key.AnalysisId}/{key.RelativePath}",
            content.Mime,
            content.Bytes.Length,
            hash));
    }

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct) =>
        Task.FromResult(Result<StoredAssetStream>.Failure(StorageErrors.NotFound));

    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Renders slides 1 and 2 successfully; on the 3rd call cancels the job's own token — the
/// same shape as a worker wall-clock timeout firing mid-carousel — and throws.</summary>
file sealed class PpCancelOnThirdSlideRenderer(CancellationTokenSource cts) : IContentRenderer
{
    private static readonly byte[] FakePngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private int _calls;

    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        _calls++;
        if (_calls == 3)
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
        }

        var asset = RenderedAsset.Create(
            spec.FormatKey, "image/png", $"slide_{_calls}.png", FakePngBytes);
        return Task.FromResult(Result.Success(asset));
    }
}

/// <summary>Control-case renderer: cancels before the very first slide finishes.</summary>
file sealed class PpCancelOnFirstSlideRenderer(CancellationTokenSource cts) : IContentRenderer
{
    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions opts, CancellationToken ct)
    {
        cts.Cancel();
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("unreachable");
    }
}

file sealed class PpCompositionService(string designedFormat, int slideCount) : IVisualCompositionService
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

    private VisualCompositionSpec BuildSpec(string formatKey, int slideNumber) =>
        new(
            FormatKey: formatKey,
            LayoutType: "story-carousel",
            Palette: DefaultPalette,
            Typography: DefaultTypo,
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string> { ["headline"] = $"Slide {slideNumber}" },
            Dimensions: DefaultDim);

    public IReadOnlyList<string> GetInferredFields() => [];
}

file sealed class PpOrchestrator(string formatKey, object payload, BrandSnapshot brand) : IContentGenerationOrchestrator
{
    public Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken ct)
    {
        var results = new Dictionary<string, FormatGenerationResult<object>>(StringComparer.OrdinalIgnoreCase)
        {
            [formatKey] = FormatGenerationResult<object>.Succeeded(payload, "fake.v1", [])
        };
        return Task.FromResult(new OrchestratorResult(OrchestratorStatus.Succeeded, results, brand));
    }
}

file sealed class PpBrandLoader(BrandSnapshot brand) : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid orgId, Guid clientId, CancellationToken ct = default) =>
        Task.FromResult(brand);
}

file sealed class PpAnalysisRepository(SingleContentAnalysisRecord record) : ISingleContentAnalysisRepository
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

file sealed class PpPackageBuilder : IGeneratedPackageBuilder
{
    public Task<BuiltPackage> BuildAsync(BuildPackageInput input, CancellationToken ct)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake-zip"));
        var manifest = new GeneratedPackageManifest(
            new Dictionary<string, FormatAssetMetadata>(), [], [], 0, input.ClientName ?? "test-client");
        return Task.FromResult(new BuiltPackage(stream, stream.Length, manifest, "test-package.zip"));
    }
}

file sealed class PpPackageStorage : IGeneratedPackageStorage
{
    public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
        Task.FromResult(new StoredPackageRef("fake-storage://test"));
    public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream());
    public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct) => Task.CompletedTask;
}

file sealed class PpPackageRepository : IGeneratedPackageRepository
{
    public Task InsertAsync(GeneratedPackage p, CancellationToken ct) => Task.CompletedTask;
    public Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct) => Task.FromResult<GeneratedPackage?>(null);
}
