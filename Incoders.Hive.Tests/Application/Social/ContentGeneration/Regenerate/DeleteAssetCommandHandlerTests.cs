using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Regenerate;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Regenerate;

/// <summary>
/// Tests for <see cref="DeleteAssetCommandHandler"/>: single-asset deletion, folder
/// cascade when the last asset is removed, tenant isolation, and idempotency.
/// </summary>
public sealed class DeleteAssetCommandHandlerTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOrgId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ResultId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static string Prefix => $"{OrgId}/{ClientId}/{AnalysisId}";

    private static FormatAssetRow Row(int index, string relative) => new(
        Id: Guid.NewGuid(),
        FormatResultId: ResultId,
        AssetIndex: index,
        StoragePath: $"{Prefix}/{relative}",
        Filename: relative.Split('/')[^1],
        MimeType: "image/png",
        SizeBytes: 100,
        ContentHash: "hash",
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: DateTime.UtcNow,
        JobCreatedAtUtc: DateTime.UtcNow);

    private static CompositionSpecEnvelope Env(string markup) =>
        new(SchemaVersion: 1, Markup: markup, Canvas: new CanvasDimensions(1080, 1080, 96), SlotGeometry: []);

    [Fact]
    public async Task Handle_DeleteOneCarouselSlide_RemovesAssetButKeepsFolder()
    {
        var rows = new List<FormatAssetRow>
        {
            Row(1, "feed-carousel/slide_1.png"),
            Row(2, "feed-carousel/slide_2.png"),
            Row(3, "feed-carousel/slide_3.png"),
        };
        var assetRepo = new FakeAssetRepository(rows);
        var storage = new SpyStorage();
        var resultRepo = new SpyResultRepository();
        var handler = new DeleteAssetCommandHandler(storage, assetRepo, resultRepo, new StubAnalysisRepository());

        var result = await handler.Handle(
            new DeleteAssetCommand(AnalysisId, OrgId, ClientId, "feed-carousel", AssetIndex: 2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"{Prefix}/feed-carousel/slide_2.png", storage.Deleted);
        Assert.Equal(2, assetRepo.Rows.Count);
        Assert.DoesNotContain(assetRepo.Rows, r => r.AssetIndex == 2);
        Assert.Empty(resultRepo.Deleted); // folder still has slides
    }

    [Fact]
    public async Task Handle_DeleteCarouselSlide_TombstonesSpecEnvelopeWithoutRenumbering()
    {
        var rows = new List<FormatAssetRow>
        {
            Row(1, "feed-carousel/slide_1.png"),
            Row(2, "feed-carousel/slide_2.png"),
            Row(3, "feed-carousel/slide_3.png"),
        };
        var assetRepo = new FakeAssetRepository(rows);
        var seededSpec = CompositionSpecJson.Serialize([Env("a"), Env("b"), Env("c")]);
        var resultRepo = new SpyResultRepository((ResultId, seededSpec));
        var handler = new DeleteAssetCommandHandler(
            new SpyStorage(), assetRepo, resultRepo, new StubAnalysisRepository());

        var result = await handler.Handle(
            new DeleteAssetCommand(AnalysisId, OrgId, ClientId, "feed-carousel", AssetIndex: 2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(resultRepo.UpdatedSpecJson);

        // All three envelopes are preserved (no renumber / no removal) — only slide 2 is tombstoned.
        var envelopes = CompositionSpecJson.ParseList(resultRepo.UpdatedSpecJson);
        Assert.Equal(3, envelopes.Count);
        Assert.False(envelopes[0].Deleted);
        Assert.True(envelopes[1].Deleted);
        Assert.False(envelopes[2].Deleted);

        // Render/export paths see only the two surviving slides.
        Assert.Equal(2, CompositionSpecJson.ParseActiveList(resultRepo.UpdatedSpecJson).Count);
        Assert.Empty(resultRepo.Deleted); // folder kept
    }

    [Fact]
    public async Task Handle_DeleteLastAsset_CascadesAndDeletesFormatResult()
    {
        var rows = new List<FormatAssetRow> { Row(0, "story-single.png") };
        var assetRepo = new FakeAssetRepository(rows);
        var storage = new SpyStorage();
        var resultRepo = new SpyResultRepository();
        var handler = new DeleteAssetCommandHandler(storage, assetRepo, resultRepo, new StubAnalysisRepository());

        var result = await handler.Handle(
            new DeleteAssetCommand(AnalysisId, OrgId, ClientId, "story-single", AssetIndex: 0),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains($"{Prefix}/story-single.png", storage.Deleted);
        Assert.Empty(assetRepo.Rows);
        Assert.Contains(ResultId, resultRepo.Deleted); // empty folder cascaded away
    }

    [Fact]
    public async Task Handle_CrossOrg_ReturnsNotFound()
    {
        var assetRepo = new FakeAssetRepository([Row(0, "story-single.png")]);
        var handler = new DeleteAssetCommandHandler(
            new SpyStorage(), assetRepo, new SpyResultRepository(), new StubAnalysisRepository());

        var result = await handler.Handle(
            new DeleteAssetCommand(AnalysisId, OtherOrgId, ClientId, "story-single", 0),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("content-gen.analysis-not-found", result.Error.Code);
    }

    [Fact]
    public async Task Handle_UnknownIndex_IsIdempotentSuccess()
    {
        var rows = new List<FormatAssetRow> { Row(1, "feed-carousel/slide_1.png") };
        var assetRepo = new FakeAssetRepository(rows);
        var storage = new SpyStorage();
        var handler = new DeleteAssetCommandHandler(
            storage, assetRepo, new SpyResultRepository(), new StubAnalysisRepository());

        var result = await handler.Handle(
            new DeleteAssetCommand(AnalysisId, OrgId, ClientId, "feed-carousel", AssetIndex: 99),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(assetRepo.Rows);
        Assert.Empty(storage.Deleted);
    }

    // ─── Test doubles ─────────────────────────────────────────────────────────

    private sealed class FakeAssetRepository(List<FormatAssetRow> rows) : IContentGenerationFormatAssetRepository
    {
        public List<FormatAssetRow> Rows { get; } = rows;

        public Task UpsertManyForFormatAsync(Guid formatResultId, IReadOnlyList<FormatAssetRow> assets, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<FormatAssetRow>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FormatAssetRow>>([]);

        public Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisClientAsync(Guid orgId, Guid aId, CancellationToken ct)
        {
            if (orgId == OrgId && aId == AnalysisId)
                return Task.FromResult<IReadOnlyList<FormatAssetRow>>(Rows.ToList());
            return Task.FromResult<IReadOnlyList<FormatAssetRow>>([]);
        }

        public Task DeleteByFormatResultIdAsync(Guid formatResultId, CancellationToken ct)
        {
            Rows.RemoveAll(r => r.FormatResultId == formatResultId);
            return Task.CompletedTask;
        }

        public Task DeleteByIdAsync(Guid assetId, CancellationToken ct)
        {
            Rows.RemoveAll(r => r.Id == assetId);
            return Task.CompletedTask;
        }

        public Task<int> CountByFormatResultIdAsync(Guid formatResultId, CancellationToken ct) =>
            Task.FromResult(Rows.Count(r => r.FormatResultId == formatResultId));
    }

    private sealed class SpyStorage : IGeneratedAssetStorage
    {
        public List<string> Deleted { get; } = [];

        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
            Task.FromResult(new StoredAssetRef("p", content.Mime, 0, "h"));

        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct) =>
            Task.FromResult(Result<StoredAssetStream>.Failure(StorageErrors.NotFound));

        public Task DeleteAsync(string storagePath, CancellationToken ct)
        {
            Deleted.Add(storagePath);
            return Task.CompletedTask;
        }
    }

    private sealed class SpyResultRepository : IContentGenerationFormatResultRepository
    {
        private readonly (Guid Id, string? Spec)? _seed;

        public SpyResultRepository((Guid Id, string? Spec)? seededSpec = null) => _seed = seededSpec;

        public List<Guid> Deleted { get; } = [];
        public string? UpdatedSpecJson { get; private set; }
        public Guid? UpdatedSpecResultId { get; private set; }

        public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct) =>
            Task.FromResult(result);

        public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>([]);

        public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByIdsAsync(IReadOnlySet<Guid> resultIds, CancellationToken ct)
        {
            if (_seed is { } seed && resultIds.Contains(seed.Id))
            {
                return Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>([
                    new ContentGenerationFormatResult(
                        Id: seed.Id,
                        JobId: Guid.NewGuid(),
                        FormatKey: "feed-carousel",
                        Status: "Succeeded",
                        OutputJson: null,
                        CompositionSpecJson: seed.Spec,
                        RenderedAssetUri: null,
                        InferredFieldsJson: null,
                        PromptVersion: null,
                        ErrorCode: null,
                        CreatedAt: DateTime.UtcNow,
                        UpdatedAt: DateTime.UtcNow),
                ]);
            }

            return Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>([]);
        }

        public Task DeleteByIdAsync(Guid formatResultId, CancellationToken ct)
        {
            Deleted.Add(formatResultId);
            return Task.CompletedTask;
        }

        public Task UpdateCompositionSpecAsync(Guid formatResultId, string? compositionSpecJson, CancellationToken ct)
        {
            UpdatedSpecResultId = formatResultId;
            UpdatedSpecJson = compositionSpecJson;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAnalysisRepository : ISingleContentAnalysisRepository
    {
        public Task AddAsync(SingleContentAnalysisRecord analysis, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<SingleContentAnalysisRecord?> GetByIdAsync(Guid aId, Guid orgId, CancellationToken cancellationToken = default)
        {
            if (aId == AnalysisId && orgId == OrgId)
            {
                return Task.FromResult<SingleContentAnalysisRecord?>(new SingleContentAnalysisRecord(
                    Id: AnalysisId,
                    OrganizationId: OrgId,
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
}
