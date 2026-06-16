using System.IO.Compression;
using System.Text;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Assemble;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Assemble;

/// <summary>
/// S3-T1 / S3-T2 / CRITICAL-1 / ADR-5: Assembler storage-folder routing for surface-scoped carousel keys.
///
/// story-carousel assets MUST land in story-carousel/ subfolder.
/// feed-carousel assets MUST land in feed-carousel/ subfolder.
/// Legacy "carousel" key MUST still resolve to carousel/ (regression guard).
/// These verify CRITICAL-1: writing S2 and S3 atomically prevents silent empty-ZIP regressions.
/// </summary>
public sealed class SurfaceScopedCarouselAssemblyTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClientId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    // ─── S3-T2a: story-carousel assets use story-carousel/ folder ────────────

    [Fact]
    public async Task Handle_StoryCarouselFormat_SlidesPlacedUnderStoryCarouselSubfolder()
    {
        var resultId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/story-carousel/slide_1.png", "slide_1.png"),
            MakeRow(resultId, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/story-carousel/slide_2.png", "slide_2.png"),
        };

        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/story-carousel/slide_1.png"] = "s1-bytes"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/story-carousel/slide_2.png"] = "s2-bytes"u8.ToArray(),
        };

        var handler = BuildHandler(
            files, rows,
            formatResultRepository: new ScAssFakeFormatResultRepository([
                MakeFormatResult(resultId, jobId, "story-carousel", EnvelopeArrayJson("slide-1", "slide-2")),
            ]),
            pptxAdapter: new ScAssFakePptxAdapter());

        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId,
                new AssembleSelection([new ClientSelection(ClientId1, null)]),
                SourceJobId: jobId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        // Slides MUST be in story-carousel/ not carousel/ (ADR-5)
        Assert.Contains($"{ClientId1}/story-carousel/slide_1.pptx", entries);
        Assert.Contains($"{ClientId1}/story-carousel/slide_2.pptx", entries);
        Assert.DoesNotContain($"{ClientId1}/carousel/slide_1.pptx", entries);
    }

    // ─── S3-T2b: feed-carousel assets use feed-carousel/ folder ─────────────

    [Fact]
    public async Task Handle_FeedCarouselFormat_SlidesPlacedUnderFeedCarouselSubfolder()
    {
        var resultId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/feed-carousel/slide_1.png", "slide_1.png"),
            MakeRow(resultId, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/feed-carousel/slide_2.png", "slide_2.png"),
        };

        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/feed-carousel/slide_1.png"] = "f1-bytes"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/feed-carousel/slide_2.png"] = "f2-bytes"u8.ToArray(),
        };

        var handler = BuildHandler(
            files, rows,
            formatResultRepository: new ScAssFakeFormatResultRepository([
                MakeFormatResult(resultId, jobId, "feed-carousel", EnvelopeArrayJson("slide-1", "slide-2")),
            ]),
            pptxAdapter: new ScAssFakePptxAdapter());

        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId,
                new AssembleSelection([new ClientSelection(ClientId1, null)]),
                SourceJobId: jobId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        // Slides MUST be in feed-carousel/ not carousel/ (ADR-5)
        Assert.Contains($"{ClientId1}/feed-carousel/slide_1.pptx", entries);
        Assert.Contains($"{ClientId1}/feed-carousel/slide_2.pptx", entries);
        Assert.DoesNotContain($"{ClientId1}/carousel/slide_1.pptx", entries);
    }

    // ─── S3-T1 / CRITICAL-1: story-carousel produces non-empty ZIP ───────────

    [Fact]
    public async Task Handle_StoryCarouselFormat_ZipIsNonEmpty()
    {
        // CRITICAL-1 regression guard: story-carousel MUST produce a non-empty ZIP.
        // If S2 and S3 are not shipped atomically, the assembler won't find the correct format key
        // in the routing logic, causing empty or absent manifest for these new keys.
        var resultId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/story-carousel/slide_1.png", "slide_1.png"),
        };

        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/story-carousel/slide_1.png"] = "s1"u8.ToArray(),
        };

        var handler = BuildHandler(
            files, rows,
            formatResultRepository: new ScAssFakeFormatResultRepository([
                MakeFormatResult(resultId, jobId, "story-carousel", EnvelopeArrayJson("slide-1")),
            ]),
            pptxAdapter: new ScAssFakePptxAdapter());

        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId,
                new AssembleSelection([new ClientSelection(ClientId1, null)]),
                SourceJobId: jobId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        // Must contain manifest.json AND at least one story-carousel entry
        Assert.Contains("manifest.json", entries);
        Assert.True(entries.Any(e => e.Contains("story-carousel")),
            "Expected at least one entry under story-carousel/");
    }

    // ─── S3 regression: legacy "carousel" still uses carousel/ folder ─────────

    [Fact]
    public async Task Handle_LegacyCarouselFormat_SlidesStillUnderCarouselFolder()
    {
        // ADR-5 backward-compat: legacy "carousel" rows MUST still appear in carousel/
        var resultId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png", "slide_1.png"),
            MakeRow(resultId, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png", "slide_2.png"),
        };

        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png"] = "c1"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png"] = "c2"u8.ToArray(),
        };

        var handler = BuildHandler(
            files, rows,
            formatResultRepository: new ScAssFakeFormatResultRepository([
                MakeFormatResult(resultId, jobId, "carousel", EnvelopeArrayJson("slide-1", "slide-2")),
            ]),
            pptxAdapter: new ScAssFakePptxAdapter());

        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId,
                new AssembleSelection([new ClientSelection(ClientId1, null)]),
                SourceJobId: jobId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        // Legacy carousel MUST still be in carousel/ (ADR-5 regression guard)
        Assert.Contains($"{ClientId1}/carousel/slide_1.pptx", entries);
        Assert.Contains($"{ClientId1}/carousel/slide_2.pptx", entries);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private AssembleGeneratedPackageQueryHandler BuildHandler(
        Dictionary<string, byte[]> storageFiles,
        IReadOnlyList<FormatAssetRow> rows,
        IContentGenerationFormatResultRepository? formatResultRepository = null,
        IPptxExportAdapter? pptxAdapter = null)
    {
        var storage = new ScAssFakeAssetStorage(storageFiles);
        var assetRepo = new ScAssFakeAssetRepository(rows, OrgId, AnalysisId);
        var analysisRepo = new ScAssFakeAnalysisRepository(OrgId, AnalysisId);
        var clientRepo = new ScAssFakeClientRepository();
        return new AssembleGeneratedPackageQueryHandler(
            storage, assetRepo, analysisRepo, clientRepo,
            formatResultRepository, pptxAdapter);
    }

    private static FormatAssetRow MakeRow(
        Guid formatResultId, int index, string storagePath, string filename) =>
        new(
            Id: Guid.NewGuid(),
            FormatResultId: formatResultId,
            AssetIndex: index,
            StoragePath: storagePath,
            Filename: filename,
            MimeType: "image/png",
            SizeBytes: 100,
            ContentHash: "abc",
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            JobCreatedAtUtc: DateTime.UtcNow);

    private static ContentGenerationFormatResult MakeFormatResult(
        Guid id, Guid jobId, string formatKey, string? compositionSpecJson = null) =>
        new(
            Id: id,
            JobId: jobId,
            FormatKey: formatKey,
            Status: "Rendered",
            OutputJson: null,
            CompositionSpecJson: compositionSpecJson,
            RenderedAssetUri: null,
            InferredFieldsJson: null,
            PromptVersion: "test.v1",
            ErrorCode: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow);

    private static string EnvelopeJson(string marker) =>
        $$"""{"schemaVersion":1,"markup":"<div data-slot=\"headline\">{{marker}}</div>","canvas":{"width":1080,"height":1920,"dpi":96},"slotGeometry":[]}""";

    private static string EnvelopeArrayJson(params string[] markers) =>
        "[" + string.Join(",", markers.Select(EnvelopeJson)) + "]";
}

// ─── File-scoped test doubles ──────────────────────────────────────────────────
// Prefixed "ScAss" to avoid duplicate-file-class collisions with the existing fakes.

file sealed class ScAssFakeAssetStorage(Dictionary<string, byte[]> files) : IGeneratedAssetStorage
{
    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct) =>
        throw new NotSupportedException("Not needed in assemble tests.");

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        if (files.TryGetValue(storagePath, out var bytes))
        {
            return Task.FromResult(Result.Success(
                new StoredAssetStream(new MemoryStream(bytes), "application/octet-stream", bytes.Length)));
        }

        return Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}

file sealed class ScAssFakeAssetRepository(
    IReadOnlyList<FormatAssetRow> rows,
    Guid organizationId,
    Guid analysisId) : IContentGenerationFormatAssetRepository
{
    public Task UpsertManyForFormatAsync(Guid formatResultId, IReadOnlyList<FormatAssetRow> assets, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<FormatAssetRow>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormatAssetRow>>([]);

    public Task<IReadOnlyList<FormatAssetRow>> ListByAnalysisClientAsync(Guid orgId, Guid aId, CancellationToken ct)
    {
        if (orgId == organizationId && aId == analysisId)
            return Task.FromResult(rows);
        return Task.FromResult<IReadOnlyList<FormatAssetRow>>([]);
    }

    public Task DeleteByFormatResultIdAsync(Guid formatResultId, CancellationToken ct) =>
        Task.CompletedTask;
}

file sealed class ScAssFakeAnalysisRepository(Guid ownerOrgId, Guid analysisId) : ISingleContentAnalysisRepository
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

    public Task UpdateStatusAsync(Guid aId, Guid orgId, SingleContentAnalysisStatus status,
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

    public Task<IReadOnlyList<string>?> DeleteOwnedByUserAsync(Guid aId, Guid orgId, Guid userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>?>([]);
}

file sealed class ScAssFakeClientRepository : ISocialClientRepository
{
    public Task<Client?> GetByIdAsync(ClientId id, Guid organizationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<Client?>(null);

    public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(SocialClientQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<(IReadOnlyList<Client>, int)>(([], 0));

    public Task AddAsync(Client client, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Update(Client client) { }
}

file sealed class ScAssFakeFormatResultRepository(IReadOnlyList<ContentGenerationFormatResult> results)
    : IContentGenerationFormatResultRepository
{
    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct) =>
        Task.FromResult(result);

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>(
            results.Where(r => r.JobId == jobId).ToList());

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByIdsAsync(
        IReadOnlySet<Guid> resultIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>(
            results.Where(r => resultIds.Contains(r.Id)).ToList());
}

file sealed class ScAssFakePptxAdapter : IPptxExportAdapter
{
    public Task<Result<byte[]>> BuildAsync(CompositionSpecEnvelope envelope, CancellationToken ct) =>
        Task.FromResult(Result.Success(Encoding.UTF8.GetBytes($"pptx:{envelope.Markup}")));

    public Task<Result<byte[]>> BuildManyAsync(IReadOnlyList<CompositionSpecEnvelope> envelopes, CancellationToken ct) =>
        Task.FromResult(Result.Success(Encoding.UTF8.GetBytes(
            string.Join("\n", envelopes.Select(e => $"pptx:{e.Markup}")))));
}
