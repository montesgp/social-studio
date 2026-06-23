using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.PackageListing;
using Incoders.Hive.Domain.Social.SingleAnalyzer;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.PackageListing;

/// <summary>
/// T6.1 — TDD RED tests for <see cref="ListAnalysisPackagesQueryHandler"/>.
/// Verifies: per-client tree returned from DB; carousel slides as children;
/// cross-org → NotFound; empty result; multiple clients.
/// </summary>
public sealed class ListAnalysisPackagesQueryHandlerTests
{
    private static readonly Guid OrgId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOrgId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid AnalysisId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ClientId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ResultId1 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ResultId2 = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid ResultId3 = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static FormatAssetRow MakeRow(
        Guid formatResultId,
        int index,
        string storagePath,
        string filename,
        string mime = "image/png",
        DateTime? updatedAtUtc = null,
        DateTime? jobCreatedAtUtc = null) =>
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
            UpdatedAt: updatedAtUtc ?? DateTime.UtcNow,
            JobCreatedAtUtc: jobCreatedAtUtc ?? DateTime.UtcNow);

    private static ListAnalysisPackagesQueryHandler BuildHandler(
        IReadOnlyList<FormatAssetRow> rows,
        Guid ownerOrgId,
        Guid analysisId)
    {
        var assetRepo = new StubAssetRepository(rows, ownerOrgId, analysisId);
        var analysisRepo = new StubAnalysisRepository(ownerOrgId, analysisId);
        return new ListAnalysisPackagesQueryHandler(assetRepo, analysisRepo);
    }

    // ─── T6.1.1 — Single-client, single-asset row → tree with one client entry ─

    [Fact]
    public async Task Handle_SingleClientSingleFormat_ReturnsSingleClientEntry()
    {
        var rows = new List<FormatAssetRow>
        {
            MakeRow(ResultId1, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/story.png", "story.png"),
        };

        var handler = BuildHandler(rows, OrgId, AnalysisId);
        var result = await handler.Handle(
            new ListAnalysisPackagesQuery(AnalysisId, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var pkg = result.Value;
        Assert.Single(pkg.Clients);
        Assert.Equal(ClientId1, pkg.Clients[0].ClientId);
        Assert.Single(pkg.Clients[0].Formats);
        Assert.Equal("story", pkg.Clients[0].Formats[0].FormatKey);
        Assert.Equal("story.png", pkg.Clients[0].Formats[0].Filename);
        Assert.Equal("image/png", pkg.Clients[0].Formats[0].Mime);
        Assert.Single(pkg.Clients[0].Formats[0].Assets);
        Assert.Equal(0, pkg.Clients[0].Formats[0].Assets[0].Index);
        Assert.Equal("story.png", pkg.Clients[0].Formats[0].Assets[0].Filename);
        Assert.Equal($"{OrgId}/{ClientId1}/{AnalysisId}/story.png",
            pkg.Clients[0].Formats[0].Assets[0].StoragePath);
    }

    // ─── T6.1.2 — Two clients → tree has two entries, correctly separated ────

    [Fact]
    public async Task Handle_TwoClients_ReturnsTwoClientEntries()
    {
        var rows = new List<FormatAssetRow>
        {
            MakeRow(ResultId1, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/story.png", "story.png"),
            MakeRow(ResultId2, 0, $"{OrgId}/{ClientId2}/{AnalysisId}/reel-script.txt", "reel-script.txt", "text/plain"),
        };

        var handler = BuildHandler(rows, OrgId, AnalysisId);
        var result = await handler.Handle(
            new ListAnalysisPackagesQuery(AnalysisId, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Clients.Count);

        var client1 = result.Value.Clients.Single(c => c.ClientId == ClientId1);
        var client2 = result.Value.Clients.Single(c => c.ClientId == ClientId2);

        Assert.Single(client1.Formats);
        Assert.Equal("story", client1.Formats[0].FormatKey);

        Assert.Single(client2.Formats);
        Assert.Equal("reel-script", client2.Formats[0].FormatKey);
        Assert.Equal("text/plain", client2.Formats[0].Mime);
    }

    // ─── T6.1.3 — Carousel slides: grouped under single format with N assets ──

    [Fact]
    public async Task Handle_CarouselSlides_GroupedUnderSingleFormat()
    {
        var rows = new List<FormatAssetRow>
        {
            MakeRow(ResultId1, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png", "slide_1.png"),
            MakeRow(ResultId1, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png", "slide_2.png"),
            MakeRow(ResultId1, 3, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_3.png", "slide_3.png"),
        };

        var handler = BuildHandler(rows, OrgId, AnalysisId);
        var result = await handler.Handle(
            new ListAnalysisPackagesQuery(AnalysisId, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var client = Assert.Single(result.Value.Clients);
        var format = Assert.Single(client.Formats);
        Assert.Equal("carousel", format.FormatKey);
        Assert.Equal(3, format.Assets.Count);
        Assert.Equal(1, format.Assets[0].Index);
        Assert.Equal("slide_1.png", format.Assets[0].Filename);
        Assert.Equal(2, format.Assets[1].Index);
        Assert.Equal(3, format.Assets[2].Index);
    }

    // ─── T6.1.3b — Multiple format versions keep only the latest result ───────

    [Fact]
    public async Task Handle_MultipleCarouselVersions_ReturnsOnlyLatestSlides()
    {
        var oldResultId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var newResultId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var oldJobCreatedAt = DateTime.UtcNow.AddMinutes(-15);
        var newJobCreatedAt = DateTime.UtcNow.AddMinutes(-5);

        var rows = new List<FormatAssetRow>
        {
            // Older carousel had 5 slides.
            MakeRow(oldResultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png", "slide_1.png", jobCreatedAtUtc: oldJobCreatedAt),
            MakeRow(oldResultId, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png", "slide_2.png", jobCreatedAtUtc: oldJobCreatedAt),
            MakeRow(oldResultId, 3, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_3.png", "slide_3.png", jobCreatedAtUtc: oldJobCreatedAt),
            MakeRow(oldResultId, 4, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_4.png", "slide_4.png", jobCreatedAtUtc: oldJobCreatedAt),
            MakeRow(oldResultId, 5, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_5.png", "slide_5.png", jobCreatedAtUtc: oldJobCreatedAt),

            // Latest carousel replaced it with 3 slides.
            MakeRow(newResultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png", "slide_1.png", jobCreatedAtUtc: newJobCreatedAt),
            MakeRow(newResultId, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png", "slide_2.png", jobCreatedAtUtc: newJobCreatedAt),
            MakeRow(newResultId, 3, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_3.png", "slide_3.png", jobCreatedAtUtc: newJobCreatedAt),
        };

        var handler = BuildHandler(rows, OrgId, AnalysisId);
        var result = await handler.Handle(
            new ListAnalysisPackagesQuery(AnalysisId, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var client = Assert.Single(result.Value.Clients);
        var carousel = Assert.Single(client.Formats, f => f.FormatKey == "carousel");
        Assert.Equal(3, carousel.Assets.Count);
        Assert.Equal([1, 2, 3], carousel.Assets.Select(a => a.Index).ToArray());
        Assert.DoesNotContain(carousel.Assets, asset => asset.Filename == "slide_4.png" || asset.Filename == "slide_5.png");
    }

    // ─── T6.1.4 — Cross-org access → NotFound (tenant isolation) ─────────────

    [Fact]
    public async Task Handle_CrossOrgRequest_ReturnsNotFound()
    {
        var rows = new List<FormatAssetRow>
        {
            MakeRow(ResultId1, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/story.png", "story.png"),
        };

        // Handler built for OrgId, but query uses OtherOrgId
        var assetRepo = new StubAssetRepository(rows, OrgId, AnalysisId);
        var analysisRepo = new StubAnalysisRepository(OrgId, AnalysisId); // only OrgId can see this analysis
        var handler = new ListAnalysisPackagesQueryHandler(assetRepo, analysisRepo);

        var result = await handler.Handle(
            new ListAnalysisPackagesQuery(AnalysisId, OtherOrgId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("content-gen.analysis-not-found", result.Error.Code);
    }

    // ─── T6.1.5 — No assets for analysis → empty clients list ────────────────

    [Fact]
    public async Task Handle_NoAssets_ReturnsEmptyClientsList()
    {
        var handler = BuildHandler([], OrgId, AnalysisId);
        var result = await handler.Handle(
            new ListAnalysisPackagesQuery(AnalysisId, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Clients);
    }

    // ─── T6.1.6 — Mixed formats for same client → all grouped correctly ───────

    [Fact]
    public async Task Handle_MultipleFormatsForSameClient_AllGroupedUnderSameClient()
    {
        var rows = new List<FormatAssetRow>
        {
            MakeRow(ResultId1, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/story.png", "story.png"),
            MakeRow(ResultId2, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/reel-script.txt", "reel-script.txt", "text/plain"),
            MakeRow(ResultId3, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png", "slide_1.png"),
            MakeRow(ResultId3, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png", "slide_2.png"),
        };

        var handler = BuildHandler(rows, OrgId, AnalysisId);
        var result = await handler.Handle(
            new ListAnalysisPackagesQuery(AnalysisId, OrgId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var client = Assert.Single(result.Value.Clients);
        Assert.Equal(ClientId1, client.ClientId);
        Assert.Equal(3, client.Formats.Count); // story, reel-script, carousel
        var carousel = client.Formats.Single(f => f.FormatKey == "carousel");
        Assert.Equal(2, carousel.Assets.Count);
    }
}

// ─── Test doubles ──────────────────────────────────────────────────────────────

file sealed class StubAssetRepository(
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

file sealed class StubAnalysisRepository(Guid ownerOrgId, Guid analysisId) : ISingleContentAnalysisRepository
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
