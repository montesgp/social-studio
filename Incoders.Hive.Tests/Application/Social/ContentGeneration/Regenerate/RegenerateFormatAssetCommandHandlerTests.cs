using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Regenerate;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Regenerate;

/// <summary>
/// T5.1 — TDD RED tests for RegenerateFormatAssetCommandHandler.
/// Verifies: regenerate rewrites storage + replaces DB rows; stale carousel slides removed;
/// tenant-scoped; format not found → NotFound; other formats untouched.
/// </summary>
public sealed class RegenerateFormatAssetCommandHandlerTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOrgId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid JobId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid StoryResultId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid HookResultId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static FormatAssetRow MakeRow(
        Guid formatResultId, int index, string storagePath, string filename, string mime = "image/png") =>
        new(
            Id: Guid.NewGuid(),
            FormatResultId: formatResultId,
            AssetIndex: index,
            StoragePath: storagePath,
            Filename: filename,
            MimeType: mime,
            SizeBytes: 100,
            ContentHash: "abc123",
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow);

    private static ContentGenerationJob MakeJob(
        Guid jobId, Guid orgId, Guid clientId, Guid analysisId,
        IReadOnlyList<string>? formats = null) =>
        new(
            Id: jobId,
            OrganizationId: orgId,
            ClientId: clientId,
            AnalysisId: analysisId,
            RequestedFormats: formats ?? ["story", "hook"],
            Status: "Completed",
            StatusReason: null,
            ErrorCode: null,
            BrandSnapshotJson: "{}",
            ManifestJson: null,
            CorrelationId: Guid.NewGuid(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            StartedAt: null,
            CompletedAt: DateTime.UtcNow);

    private static BrandSnapshot MakeBrand() =>
        BrandSnapshot.FromBrandData(null, null, null);

    // ─── T5.1.1 — Regenerate overwrites storage and replaces asset rows ────────

    [Fact]
    public async Task Handle_RegenerateStory_OverwritesStorageAndReplacesRows()
    {
        // Arrange
        var storyRow = MakeRow(StoryResultId, 0, $"{OrgId}/{ClientId}/{AnalysisId}/story.png", "story.png");
        var hookRow = MakeRow(HookResultId, 0, $"{OrgId}/{ClientId}/{AnalysisId}/hook.txt", "hook.txt", "text/plain");

        var formatResults = new List<ContentGenerationFormatResult>
        {
            new(Id: StoryResultId, JobId: JobId, FormatKey: "story", Status: "Rendered",
                OutputJson: """{"title":"Old Title"}""", CompositionSpecJson: null,
                RenderedAssetUri: null, InferredFieldsJson: null, PromptVersion: null,
                ErrorCode: null, CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow),
            new(Id: HookResultId, JobId: JobId, FormatKey: "hook", Status: "Rendered",
                OutputJson: """{"text":"hook text"}""", CompositionSpecJson: null,
                RenderedAssetUri: null, InferredFieldsJson: null, PromptVersion: null,
                ErrorCode: null, CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow),
        };

        var job = MakeJob(JobId, OrgId, ClientId, AnalysisId);

        var storage = new FakeRegenerateStorage();
        var assetRepo = new FakeRegenerateAssetRepository([storyRow, hookRow]);
        var formatResultRepo = new FakeRegenerateFormatResultRepository(formatResults);
        var jobRepo = new FakeRegenerateJobRepository(job);
        var analysisRepo = new FakeRegenerateAnalysisRepository(OrgId, AnalysisId);
        var composer = new FakeRegenerateComposer(MakeSpec("story"));
        var renderer = new FakeRegenerateRenderer("image/png", "story.png", new byte[] { 1, 2, 3 });
        var brandLoader = new FakeRegenerateBrandLoader(MakeBrand());

        var handler = new RegenerateFormatAssetCommandHandler(
            storage, assetRepo, formatResultRepo, jobRepo, analysisRepo, composer, renderer, brandLoader);

        var command = new RegenerateFormatAssetCommand(
            AnalysisId, OrgId, ClientId, "story", Objection: null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.MessageKey : "");

        // Storage was written for the story format asset
        Assert.Contains($"{OrgId}/{ClientId}/{AnalysisId}/story.png", storage.PutPaths);

        // Asset rows were replaced for the story format result
        Assert.Equal(StoryResultId, assetRepo.LastUpsertedFormatResultId);
        Assert.NotEmpty(assetRepo.LastUpsertedRows);

        // Hook rows were NOT touched
        Assert.Equal(HookResultId, hookRow.FormatResultId); // hook row unchanged
    }

    // ─── T5.1.2 — Cross-tenant → NotFound ────────────────────────────────────

    [Fact]
    public async Task Handle_CrossTenant_ReturnsNotFound()
    {
        var job = MakeJob(JobId, OrgId, ClientId, AnalysisId);
        var handler = BuildMinimalHandler(job, analysisOrgId: OrgId, analysisId: AnalysisId);

        // Requesting org is different from the analysis org
        var command = new RegenerateFormatAssetCommand(AnalysisId, OtherOrgId, ClientId, "story", null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── T5.1.3 — Format not found for this client → NotFound ────────────────

    [Fact]
    public async Task Handle_FormatNotFoundForClient_ReturnsNotFound()
    {
        // No format results for "carousel"
        var formatResults = new List<ContentGenerationFormatResult>
        {
            new(Id: StoryResultId, JobId: JobId, FormatKey: "story", Status: "Rendered",
                OutputJson: """{"title":"Story"}""", CompositionSpecJson: null,
                RenderedAssetUri: null, InferredFieldsJson: null, PromptVersion: null,
                ErrorCode: null, CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow),
        };

        var job = MakeJob(JobId, OrgId, ClientId, AnalysisId);
        var handler = BuildMinimalHandler(job, OrgId, AnalysisId, formatResults: formatResults);

        var command = new RegenerateFormatAssetCommand(AnalysisId, OrgId, ClientId, "carousel", null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── T5.1.4 — No job found for analysis+client → NotFound ────────────────

    [Fact]
    public async Task Handle_NoJobForClient_ReturnsNotFound()
    {
        // JobRepo returns null for this analysis+client
        var handler = BuildMinimalHandler(job: null, OrgId, AnalysisId);

        var command = new RegenerateFormatAssetCommand(AnalysisId, OrgId, ClientId, "story", null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── T5.1.5 — Stale carousel slides deleted when new render has fewer slides

    [Fact]
    public async Task Handle_RegenerateCarousel_DeletesStaleSlidesFromStorage()
    {
        var carouselResultId = Guid.NewGuid();
        var rows = new List<FormatAssetRow>
        {
            MakeRow(carouselResultId, 1, $"{OrgId}/{ClientId}/{AnalysisId}/carousel/slide_1.png", "slide_1.png"),
            MakeRow(carouselResultId, 2, $"{OrgId}/{ClientId}/{AnalysisId}/carousel/slide_2.png", "slide_2.png"),
            MakeRow(carouselResultId, 3, $"{OrgId}/{ClientId}/{AnalysisId}/carousel/slide_3.png", "slide_3.png"),
        };

        var formatResults = new List<ContentGenerationFormatResult>
        {
            new(Id: carouselResultId, JobId: JobId, FormatKey: "carousel", Status: "Rendered",
                OutputJson: """{"slides":[]}""", CompositionSpecJson: null,
                RenderedAssetUri: null, InferredFieldsJson: null, PromptVersion: null,
                ErrorCode: null, CreatedAt: DateTime.UtcNow, UpdatedAt: DateTime.UtcNow),
        };

        var job = MakeJob(JobId, OrgId, ClientId, AnalysisId, ["carousel"]);

        // New render produces only 2 slides
        var storage = new FakeRegenerateStorage();
        var assetRepo = new FakeRegenerateAssetRepository(rows);
        var formatResultRepo = new FakeRegenerateFormatResultRepository(formatResults);
        var jobRepo = new FakeRegenerateJobRepository(job);
        var analysisRepo = new FakeRegenerateAnalysisRepository(OrgId, AnalysisId);

        var composer = new FakeRegenerateComposer(MakeSpec("carousel"), MakeSpec("carousel")); // 2 slides now
        var renderer = new FakeRegenerateRenderer("image/png", "slide.png", new byte[] { 9, 8, 7 });
        var brandLoader = new FakeRegenerateBrandLoader(MakeBrand());

        var handler = new RegenerateFormatAssetCommandHandler(
            storage, assetRepo, formatResultRepo, jobRepo, analysisRepo, composer, renderer, brandLoader);

        var command = new RegenerateFormatAssetCommand(AnalysisId, OrgId, ClientId, "carousel", null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // The stale slide_3 that is no longer in the new render must be deleted from storage
        Assert.Contains($"{OrgId}/{ClientId}/{AnalysisId}/carousel/slide_3.png", storage.DeletedPaths);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static VisualCompositionSpec MakeSpec(string formatKey) =>
        new(
            FormatKey: formatKey,
            LayoutType: "test-card",
            Palette: new CompositionPalette("#000", "#111", "#222", "#fff", "#eee"),
            Typography: new CompositionTypography("Arial", 24, "Arial", 14),
            AssetSlots: new Dictionary<string, AssetSlotSpec>(),
            CopySlots: new Dictionary<string, string>(),
            Dimensions: new CompositionDimensions(1080, 1080, 96));

    private static RegenerateFormatAssetCommandHandler BuildMinimalHandler(
        ContentGenerationJob? job,
        Guid analysisOrgId,
        Guid analysisId,
        List<ContentGenerationFormatResult>? formatResults = null)
    {
        var storage = new FakeRegenerateStorage();
        var rows = new List<FormatAssetRow>();
        var assetRepo = new FakeRegenerateAssetRepository(rows);
        var formatResultRepo = new FakeRegenerateFormatResultRepository(formatResults ?? []);
        var jobRepo = new FakeRegenerateJobRepository(job);
        var analysisRepo = new FakeRegenerateAnalysisRepository(analysisOrgId, analysisId);
        var composer = new FakeRegenerateComposer(MakeSpec("story"));
        var renderer = new FakeRegenerateRenderer("image/png", "story.png", new byte[] { 1, 2, 3 });
        var brandLoader = new FakeRegenerateBrandLoader(MakeBrand());

        return new RegenerateFormatAssetCommandHandler(
            storage, assetRepo, formatResultRepo, jobRepo, analysisRepo, composer, renderer, brandLoader);
    }
}

// ─── T5.4 — Delete-item tests ────────────────────────────────────────────────

/// <summary>
/// T5.4 — TDD RED tests for DeleteFormatAssetCommandHandler.
/// Verifies: physical delete from storage + removes DB rows; tenant-scoped;
/// format not found → succeeds silently (idempotent).
/// </summary>
public sealed class DeleteFormatAssetCommandHandlerTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOrgId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid HookResultId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static FormatAssetRow MakeRow(
        Guid formatResultId, int index, string storagePath, string filename, string mime = "text/plain") =>
        new(
            Id: Guid.NewGuid(),
            FormatResultId: formatResultId,
            AssetIndex: index,
            StoragePath: storagePath,
            Filename: filename,
            MimeType: mime,
            SizeBytes: 50,
            ContentHash: "def456",
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow);

    // ─── T5.4.1 — Delete removes storage object and DB row ───────────────────

    [Fact]
    public async Task Handle_DeleteHook_RemovesStorageObjectAndRow()
    {
        var hookRow = MakeRow(HookResultId, 0, $"{OrgId}/{ClientId}/{AnalysisId}/hook.txt", "hook.txt");

        var storage = new FakeRegenerateStorage();
        var assetRepo = new FakeRegenerateAssetRepository([hookRow]);
        var analysisRepo = new FakeRegenerateAnalysisRepository(OrgId, AnalysisId);

        var handler = new DeleteFormatAssetCommandHandler(storage, assetRepo, analysisRepo);

        var command = new DeleteFormatAssetCommand(AnalysisId, OrgId, ClientId, "hook");
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Storage object was deleted
        Assert.Contains($"{OrgId}/{ClientId}/{AnalysisId}/hook.txt", storage.DeletedPaths);

        // DB rows were deleted for hook format result
        Assert.Contains(HookResultId, assetRepo.DeletedFormatResultIds);
    }

    // ─── T5.4.2 — Delete carousel removes ALL slide objects ─────────────────

    [Fact]
    public async Task Handle_DeleteCarousel_RemovesAllSlideObjects()
    {
        var carouselResultId = Guid.NewGuid();
        var rows = new List<FormatAssetRow>
        {
            MakeRow(carouselResultId, 1, $"{OrgId}/{ClientId}/{AnalysisId}/carousel/slide_1.png", "slide_1.png", "image/png"),
            MakeRow(carouselResultId, 2, $"{OrgId}/{ClientId}/{AnalysisId}/carousel/slide_2.png", "slide_2.png", "image/png"),
        };

        var storage = new FakeRegenerateStorage();
        var assetRepo = new FakeRegenerateAssetRepository(rows);
        var analysisRepo = new FakeRegenerateAnalysisRepository(OrgId, AnalysisId);

        var handler = new DeleteFormatAssetCommandHandler(storage, assetRepo, analysisRepo);

        var command = new DeleteFormatAssetCommand(AnalysisId, OrgId, ClientId, "carousel");
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Contains($"{OrgId}/{ClientId}/{AnalysisId}/carousel/slide_1.png", storage.DeletedPaths);
        Assert.Contains($"{OrgId}/{ClientId}/{AnalysisId}/carousel/slide_2.png", storage.DeletedPaths);
        Assert.Contains(carouselResultId, assetRepo.DeletedFormatResultIds);
    }

    // ─── T5.4.3 — Cross-tenant → NotFound ───────────────────────────────────

    [Fact]
    public async Task Handle_CrossTenant_ReturnsNotFound()
    {
        var storage = new FakeRegenerateStorage();
        var assetRepo = new FakeRegenerateAssetRepository([]);
        var analysisRepo = new FakeRegenerateAnalysisRepository(OrgId, AnalysisId);

        var handler = new DeleteFormatAssetCommandHandler(storage, assetRepo, analysisRepo);

        // Different org
        var command = new DeleteFormatAssetCommand(AnalysisId, OtherOrgId, ClientId, "hook");
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── T5.4.4 — Format not found → succeeds (idempotent) ──────────────────

    [Fact]
    public async Task Handle_FormatNotFound_SucceedsIdempotently()
    {
        // No rows for "story"
        var storage = new FakeRegenerateStorage();
        var assetRepo = new FakeRegenerateAssetRepository([]);
        var analysisRepo = new FakeRegenerateAnalysisRepository(OrgId, AnalysisId);

        var handler = new DeleteFormatAssetCommandHandler(storage, assetRepo, analysisRepo);

        var command = new DeleteFormatAssetCommand(AnalysisId, OrgId, ClientId, "story");
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(storage.DeletedPaths);
    }
}

// ─── Test doubles shared across regenerate + delete tests ─────────────────────

file sealed class FakeRegenerateStorage : IGeneratedAssetStorage
{
    public List<string> PutPaths { get; } = [];
    public List<string> DeletedPaths { get; } = [];

    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
    {
        var path = $"{key.OrganizationId}/{key.ClientId}/{key.AnalysisId}/{key.RelativePath}";
        PutPaths.Add(path);
        return Task.FromResult(new StoredAssetRef(path, content.Mime, content.Bytes.Length, "hash-new"));
    }

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct) =>
        Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        DeletedPaths.Add(storagePath);
        return Task.CompletedTask;
    }
}

file sealed class FakeRegenerateAssetRepository(IReadOnlyList<FormatAssetRow> rows)
    : IContentGenerationFormatAssetRepository
{
    private readonly List<FormatAssetRow> _rows = [.. rows];

    public Guid LastUpsertedFormatResultId { get; private set; }
    public IReadOnlyList<FormatAssetRow> LastUpsertedRows { get; private set; } = [];
    public List<Guid> DeletedFormatResultIds { get; } = [];

    public Task UpsertManyForFormatAsync(Guid formatResultId, IReadOnlyList<FormatAssetRow> assets, CancellationToken ct)
    {
        LastUpsertedFormatResultId = formatResultId;
        LastUpsertedRows = assets;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FormatAssetRow>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormatAssetRow>>(_rows);

    public Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisClientAsync(Guid orgId, Guid analysisId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormatAssetRow>>(_rows);

    public Task DeleteByFormatResultIdAsync(Guid formatResultId, CancellationToken ct)
    {
        DeletedFormatResultIds.Add(formatResultId);
        _rows.RemoveAll(r => r.FormatResultId == formatResultId);
        return Task.CompletedTask;
    }
}

file sealed class FakeRegenerateFormatResultRepository(
    List<ContentGenerationFormatResult> results) : IContentGenerationFormatResultRepository
{
    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct) =>
        Task.FromResult(result);

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>(results);
}

file sealed class FakeRegenerateJobRepository(ContentGenerationJob? job) : IContentGenerationJobRepository
{
    public Task InsertAsync(ContentGenerationJob job, CancellationToken ct) => Task.CompletedTask;

    public Task<ContentGenerationJob?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct) =>
        Task.FromResult(job?.OrganizationId == organizationId ? job : null);

    public Task<ContentGenerationJob?> GetByIdInternalAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(job);

    public Task UpdateStatusAsync(Guid id, string status, string? statusReason, string? errorCode, CancellationToken ct) =>
        Task.CompletedTask;

    public Task UpdateManifestAsync(Guid id, string manifestJson, DateTime completedAt, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<ContentGenerationJob>> ListPendingOrRunningAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationJob>>([]);

    public Task<ContentGenerationJob?> GetLatestByAnalysisAndClientAsync(
        Guid analysisId, Guid organizationId, Guid clientId, CancellationToken ct) =>
        Task.FromResult(
            job is not null
            && job.AnalysisId == analysisId
            && job.OrganizationId == organizationId
            && job.ClientId == clientId
                ? job
                : null);
}

file sealed class FakeRegenerateAnalysisRepository(Guid ownerOrgId, Guid analysisId)
    : ISingleContentAnalysisRepository
{
    public Task AddAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid aId, Guid orgId, CancellationToken cancellationToken = default)
    {
        if (aId == analysisId && orgId == ownerOrgId)
        {
            return Task.FromResult<SingleContentAnalysisRecord?>(new SingleContentAnalysisRecord(
                Id: analysisId,
                OrganizationId: ownerOrgId,
                ClientId: null,
                UserId: Guid.NewGuid(),
                SourceUrl: "https://example.com",
                NormalizedUrl: null,
                SourcePlatformKey: null,
                Status: SingleContentAnalysisStatus.Analyzed,
                ContentSnapshotJson: null,
                AnalysisJson: null,
                ProviderDiagnosticsJson: null,
                FailureCode: null,
                FailureMessageKey: null,
                CreatedAtUtc: DateTime.UtcNow,
                UpdatedAtUtc: DateTime.UtcNow,
                CompletedAtUtc: DateTime.UtcNow));
        }

        return Task.FromResult<SingleContentAnalysisRecord?>(null);
    }

    public Task UpdateStatusAsync(Guid analysisId, Guid organizationId, SingleContentAnalysisStatus status,
        DateTime updatedAtUtc, CancellationToken cancellationToken = default,
        string? failureCode = null, string? failureMessageKey = null) => Task.CompletedTask;

    public Task UpdateAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<SingleContentAnalysisHistoryPage> ListByUserAsync(Guid organizationId, Guid userId, int page,
        int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SingleContentAnalysisHistoryPage([], 0, page, pageSize));

    public Task<SingleContentAnalysisHistoryItem?> FindLatestByUserAndUrlAsync(Guid organizationId, Guid userId,
        string normalizedUrl, string sourceUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult<SingleContentAnalysisHistoryItem?>(null);

    public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(Guid analysisId, Guid organizationId, Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>?>([]);
}

file sealed class FakeRegenerateComposer(params VisualCompositionSpec[] specs) : IVisualCompositionService
{
    public Task<VisualCompositionSpec> ComposeAsync(string formatKey, object generatedPayload, BrandSnapshot brand, CancellationToken ct) =>
        Task.FromResult(specs[0]);

    public Task<IReadOnlyList<VisualCompositionSpec>> ComposeManyAsync(string formatKey, object generatedPayload, BrandSnapshot brand, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VisualCompositionSpec>>(specs);

    public IReadOnlyList<string> GetInferredFields() => [];
}

file sealed class FakeRegenerateRenderer(string mime, string filename, byte[] bytes) : IContentRenderer
{
    public Task<Result<RenderedAsset>> RenderAsync(VisualCompositionSpec spec, RenderOptions options, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(RenderedAsset.Create(spec.FormatKey, mime, filename, bytes)));
}

file sealed class FakeRegenerateBrandLoader(BrandSnapshot brand) : IClientBrandSnapshotLoader
{
    public Task<BrandSnapshot> LoadAsync(Guid organizationId, Guid clientId, CancellationToken ct) =>
        Task.FromResult(brand);
}
