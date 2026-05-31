using System.IO.Compression;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Assemble;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Assemble;

/// <summary>
/// T4.1 — TDD RED tests for <see cref="AssembleGeneratedPackageQueryHandler"/>.
/// Verifies: selection across 2 clients → correct ZIP entries; missing asset (404) skipped;
/// tenant mismatch → NotFound; carousel subfolder in ZIP; empty result; filename convention.
/// </summary>
public sealed class AssembleGeneratedPackageQueryHandlerTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOrgId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ClientId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

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

    private static AssembleGeneratedPackageQueryHandler BuildHandler(
        Dictionary<string, byte[]> storageFiles,
        IReadOnlyList<FormatAssetRow> rows,
        Guid ownerOrgId,
        Guid analysisId)
    {
        var storage = new FakeAssetStorage(storageFiles);
        var assetRepo = new FakeAssetRepository(rows, ownerOrgId, analysisId);
        var analysisRepo = new FakeAnalysisRepository(ownerOrgId, analysisId);
        return new AssembleGeneratedPackageQueryHandler(storage, assetRepo, analysisRepo);
    }

    // ─── T4.1.1 — Multi-client selection → correct ZIP entries ────────────────

    [Fact]
    public async Task Handle_TwoClientSelection_ZipContainsBothClientFolders()
    {
        var resultId1 = Guid.NewGuid();
        var resultId2 = Guid.NewGuid();

        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId1, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/flyer.png", "flyer.png"),
            MakeRow(resultId2, 0, $"{OrgId}/{ClientId2}/{AnalysisId}/reel-script.txt", "reel-script.txt", "text/plain"),
        };

        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/flyer.png"] = "client1-flyer-bytes"u8.ToArray(),
            [$"{OrgId}/{ClientId2}/{AnalysisId}/reel-script.txt"] = "client2-reel-bytes"u8.ToArray(),
        };

        var handler = BuildHandler(files, rows, OrgId, AnalysisId);

        var selection = new AssembleSelection([
            new ClientSelection(ClientId1, null),
            new ClientSelection(ClientId2, null),
        ]);

        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains(entries, e => e.StartsWith($"{ClientId1}/") && e.Contains("flyer.png"));
        Assert.Contains(entries, e => e.StartsWith($"{ClientId2}/") && e.Contains("reel-script.txt"));
    }

    // ─── T4.1.2 — Missing asset (404 from storage) → skipped, ZIP still succeeds

    [Fact]
    public async Task Handle_MissingAssetInStorage_SkipsEntryAndSucceeds()
    {
        var resultId = Guid.NewGuid();

        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/flyer.png", "flyer.png"),
            MakeRow(resultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/hook.txt", "hook.txt", "text/plain"),
        };

        // hook.txt NOT in storage → storage returns NotFound for it
        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/flyer.png"] = "flyer-bytes"u8.ToArray(),
        };

        var handler = BuildHandler(files, rows, OrgId, AnalysisId);

        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains(entries, e => e.Contains("flyer.png"));
        Assert.DoesNotContain(entries, e => e.Contains("hook.txt"));
    }

    // ─── T4.1.3 — Cross-tenant → NotFound ────────────────────────────────────

    [Fact]
    public async Task Handle_CrossTenantAnalysis_ReturnsNotFound()
    {
        var handler = BuildHandler(new Dictionary<string, byte[]>(), [], OrgId, AnalysisId);

        // Request with OtherOrgId → analysis not found for that org
        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OtherOrgId, selection),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ─── T4.1.4 — Carousel assets placed under carousel/ subfolder ───────────

    [Fact]
    public async Task Handle_CarouselAssets_PlacedUnderCarouselSubfolder()
    {
        var resultId = Guid.NewGuid();

        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png", "carousel/slide_1.png"),
            MakeRow(resultId, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png", "carousel/slide_2.png"),
        };

        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png"] = "slide1-bytes"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png"] = "slide2-bytes"u8.ToArray(),
        };

        var handler = BuildHandler(files, rows, OrgId, AnalysisId);

        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains(entries, e => e.Contains("carousel/slide_1.png"));
        Assert.Contains(entries, e => e.Contains("carousel/slide_2.png"));
    }

    // ─── T4.1.5 — Empty storage → valid empty ZIP ────────────────────────────

    [Fact]
    public async Task Handle_NoAssetsInStorage_ReturnsSuccessWithEmptyZip()
    {
        var handler = BuildHandler(new Dictionary<string, byte[]>(), [], OrgId, AnalysisId);

        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        Assert.Empty(zip.Entries);
    }

    // ─── T4.1.6 — Filename includes analysisId ────────────────────────────────

    [Fact]
    public async Task Handle_Success_FilenameContainsAnalysisId()
    {
        var handler = BuildHandler(new Dictionary<string, byte[]>(), [], OrgId, AnalysisId);

        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(AnalysisId.ToString(), result.Value.DownloadFilename);
    }
}

// ─── Test doubles ──────────────────────────────────────────────────────────────

file sealed class FakeAssetStorage(Dictionary<string, byte[]> files) : IGeneratedAssetStorage
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

file sealed class FakeAssetRepository(
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

file sealed class FakeAnalysisRepository(Guid ownerOrgId, Guid analysisId) : ISingleContentAnalysisRepository
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
