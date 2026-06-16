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

    private static ContentGenerationFormatResult MakeFormatResult(
        Guid id,
        Guid jobId,
        string formatKey,
        string? compositionSpecJson = null,
        string? outputJson = null,
        string? inferredFieldsJson = null,
        DateTime? updatedAtUtc = null) =>
        new(
            Id: id,
            JobId: jobId,
            FormatKey: formatKey,
            Status: "Rendered",
            OutputJson: outputJson,
            CompositionSpecJson: compositionSpecJson,
            RenderedAssetUri: null,
            InferredFieldsJson: inferredFieldsJson,
            PromptVersion: "test.v1",
            ErrorCode: null,
            CreatedAt: updatedAtUtc ?? DateTime.UtcNow,
            UpdatedAt: updatedAtUtc ?? DateTime.UtcNow);

    private static string EnvelopeJson(string marker) =>
        $$"""{"schemaVersion":1,"markup":"<div data-slot=\"headline\">{{marker}}</div>","canvas":{"width":1080,"height":1920,"dpi":96},"slotGeometry":[]}""";

    private static string EnvelopeArrayJson(params string[] markers) =>
        "[" + string.Join(",", markers.Select(EnvelopeJson)) + "]";

    private static byte[] PptxBytesForEnvelope(CompositionSpecEnvelope envelope) =>
        Encoding.UTF8.GetBytes($"pptx:{envelope.Markup}");

    private static async Task<byte[]> ReadEntryBytesAsync(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        Assert.NotNull(entry);

        await using var stream = entry!.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private static async Task<string> ReadEntryTextAsync(ZipArchive zip, string entryName) =>
        Encoding.UTF8.GetString(await ReadEntryBytesAsync(zip, entryName));

    private static AssembleGeneratedPackageQueryHandler BuildHandler(
        Dictionary<string, byte[]> storageFiles,
        IReadOnlyList<FormatAssetRow> rows,
        Guid ownerOrgId,
        Guid analysisId,
        ISocialClientRepository? clientRepository = null,
        IContentGenerationFormatResultRepository? formatResultRepository = null,
        IPptxExportAdapter? pptxAdapter = null)
    {
        var storage = new FakeAssetStorage(storageFiles);
        var assetRepo = new FakeAssetRepository(rows, ownerOrgId, analysisId);
        var analysisRepo = new FakeAnalysisRepository(ownerOrgId, analysisId);
        var clientRepo = clientRepository ?? new FakeClientRepository([]);
        return new AssembleGeneratedPackageQueryHandler(
            storage,
            assetRepo,
            analysisRepo,
            clientRepo,
            formatResultRepository,
            pptxAdapter);
    }

    // ─── T4.1.1 — Multi-client selection → correct ZIP entries ────────────────

    [Fact]
    public async Task Handle_TwoClientSelection_ZipContainsBothClientFolders()
    {
        // The clients are intentionally absent from the (default empty) FakeClientRepository,
        // so this deliberately exercises the GUID-fallback folder path (pre-PKG-02 behavior),
        // not an oversight in test setup.
        var resultId1 = Guid.NewGuid();
        var resultId2 = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId1, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/story.png", "story.png"),
            MakeRow(resultId2, 0, $"{OrgId}/{ClientId2}/{AnalysisId}/reel-script.md", "reel-script.md", "text/markdown"),
        };

        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/story.png"] = "client1-flyer-bytes"u8.ToArray(),
            [$"{OrgId}/{ClientId2}/{AnalysisId}/reel-script.md"] = "client2-reel-bytes"u8.ToArray(),
        };

        var formatResults = new[]
        {
            MakeFormatResult(resultId1, jobId, "story", EnvelopeJson("client-1-flyer")),
            MakeFormatResult(resultId2, jobId, "reel-script"),
        };

        var handler = BuildHandler(
            files,
            rows,
            OrgId,
            AnalysisId,
            formatResultRepository: new FakeFormatResultRepository(formatResults),
            pptxAdapter: new FakePptxAdapter(PptxBytesForEnvelope));

        var selection = new AssembleSelection([
            new ClientSelection(ClientId1, null),
            new ClientSelection(ClientId2, null),
        ]);

        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection, SourceJobId: jobId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains($"{ClientId1}/story/story.pptx", entries);
        Assert.Contains($"{ClientId2}/script/script.md", entries);
        Assert.DoesNotContain($"{ClientId1}/story.png", entries);
        Assert.DoesNotContain($"{ClientId2}/reel-script.md", entries);
    }

    // ─── T4.1.2 — Missing asset (404 from storage) → skipped, ZIP still succeeds

    [Fact]
    public async Task Handle_MissingAssetInStorage_SkipsEntryAndSucceeds()
    {
        var resultId = Guid.NewGuid();

        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/reel-script.md", "reel-script.md", "text/markdown"),
            MakeRow(resultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/hook.txt", "hook.txt", "text/plain"),
        };

        // hook.txt NOT in storage → storage returns NotFound for it
        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/reel-script.md"] = "script-bytes"u8.ToArray(),
        };

        var handler = BuildHandler(files, rows, OrgId, AnalysisId);

        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains($"{ClientId1}/script/script.md", entries);
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
        var jobId = Guid.NewGuid();

        var rows = new List<FormatAssetRow>
        {
            // The durable asset rows store carousel filenames as leaf names; the storage path
            // is authoritative for preserving the carousel/ subfolder in assembled ZIPs.
            MakeRow(resultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png", "slide_1.png"),
            MakeRow(resultId, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png", "slide_2.png"),
        };

        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png"] = "slide1-bytes"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png"] = "slide2-bytes"u8.ToArray(),
        };

        var handler = BuildHandler(
            files,
            rows,
            OrgId,
            AnalysisId,
            formatResultRepository: new FakeFormatResultRepository([
                MakeFormatResult(resultId, jobId, "carousel", EnvelopeArrayJson("slide-one", "slide-two")),
            ]),
            pptxAdapter: new FakePptxAdapter(PptxBytesForEnvelope));

        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection, SourceJobId: jobId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains($"{ClientId1}/carousel/slide_1.pptx", entries);
        Assert.Contains($"{ClientId1}/carousel/slide_2.pptx", entries);
        Assert.DoesNotContain($"{ClientId1}/carousel/slide_1.png", entries);
        Assert.DoesNotContain($"{ClientId1}/carousel/slide_2.png", entries);
        Assert.DoesNotContain($"{ClientId1}/slide_1.png", entries);
    }

    // ─── T4.1.4b — Older carousel rows are excluded from assembled ZIP ────────

    [Fact]
    public async Task Handle_MultipleCarouselVersions_UsesLatestVersionOnly()
    {
        var oldResultId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var newResultId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var jobId = Guid.NewGuid();
        var oldJobCreatedAt = DateTime.UtcNow.AddMinutes(-20);
        var newJobCreatedAt = DateTime.UtcNow.AddMinutes(-2);

        var rows = new List<FormatAssetRow>
        {
            // Older render had five slides.
            MakeRow(oldResultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png", "slide_1.png", jobCreatedAtUtc: oldJobCreatedAt),
            MakeRow(oldResultId, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png", "slide_2.png", jobCreatedAtUtc: oldJobCreatedAt),
            MakeRow(oldResultId, 3, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_3.png", "slide_3.png", jobCreatedAtUtc: oldJobCreatedAt),
            MakeRow(oldResultId, 4, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_4.png", "slide_4.png", jobCreatedAtUtc: oldJobCreatedAt),
            MakeRow(oldResultId, 5, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_5.png", "slide_5.png", jobCreatedAtUtc: oldJobCreatedAt),

            // Latest render replaced it with three slides.
            MakeRow(newResultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png", "slide_1.png", jobCreatedAtUtc: newJobCreatedAt),
            MakeRow(newResultId, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png", "slide_2.png", jobCreatedAtUtc: newJobCreatedAt),
            MakeRow(newResultId, 3, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_3.png", "slide_3.png", jobCreatedAtUtc: newJobCreatedAt),
        };

        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png"] = "new-slide-1"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png"] = "new-slide-2"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_3.png"] = "new-slide-3"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_4.png"] = "stale-slide-4"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_5.png"] = "stale-slide-5"u8.ToArray(),
        };

        var handler = BuildHandler(
            files,
            rows,
            OrgId,
            AnalysisId,
            formatResultRepository: new FakeFormatResultRepository([
                MakeFormatResult(newResultId, jobId, "carousel", EnvelopeArrayJson("new-slide-1", "new-slide-2", "new-slide-3"), updatedAtUtc: newJobCreatedAt),
            ]),
            pptxAdapter: new FakePptxAdapter(PptxBytesForEnvelope));
        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection, SourceJobId: jobId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).OrderBy(e => e).ToList();

        Assert.Equal(4, entries.Count);
        Assert.Equal(
            [
                $"{ClientId1}/carousel/slide_1.pptx",
                $"{ClientId1}/carousel/slide_2.pptx",
                $"{ClientId1}/carousel/slide_3.pptx",
                "manifest.json",
            ],
            entries);
        Assert.DoesNotContain($"{ClientId1}/carousel/slide_4.pptx", entries);
        Assert.DoesNotContain($"{ClientId1}/carousel/slide_5.pptx", entries);
    }

    // ─── T4.1.5 — Empty storage → valid empty ZIP ────────────────────────────

    [Fact]
    public async Task Handle_NoAssetsInStorage_ReturnsSuccessWithManifestOnlyZip()
    {
        var handler = BuildHandler(new Dictionary<string, byte[]>(), [], OrgId, AnalysisId);

        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        Assert.Equal(["manifest.json"], zip.Entries.Select(e => e.FullName));
    }

    [Fact]
    public async Task Handle_AssembledZip_UsesClientFirstFormatFoldersManifestAndEditableFormatFiles()
    {
        var flyerResultId = Guid.NewGuid();
        var carouselResultId = Guid.NewGuid();
        var scriptResultId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var rows = new List<FormatAssetRow>
        {
            MakeRow(flyerResultId, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/story.png", "story.png"),
            MakeRow(carouselResultId, 1, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png", "slide_1.png"),
            MakeRow(carouselResultId, 2, $"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png", "slide_2.png"),
            MakeRow(scriptResultId, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/reel-script.md", "reel-script.md", "text/markdown"),
        };
        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/story.png"] = "story-bytes"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_1.png"] = "slide-1-bytes"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/carousel/slide_2.png"] = "slide-2-bytes"u8.ToArray(),
            [$"{OrgId}/{ClientId1}/{AnalysisId}/reel-script.md"] = "# Script\n\nUse this on social."u8.ToArray(),
        };
        var client = Client.Restore(
            new ClientId(ClientId1), OrgId, "Incoders",
            null, true, DateTime.UtcNow, DateTime.UtcNow);

        var handler = BuildHandler(
            files,
            rows,
            OrgId,
            AnalysisId,
            new FakeClientRepository([client]),
            formatResultRepository: new FakeFormatResultRepository([
                MakeFormatResult(
                    flyerResultId,
                    jobId,
                    "story",
                    EnvelopeJson("story"),
                    """{"publicationDescription":"Caption for the client account."}""",
                    """["brand.voice"]"""),
                MakeFormatResult(
                    carouselResultId,
                    jobId,
                    "carousel",
                    EnvelopeArrayJson("carousel-one", "carousel-two")),
                MakeFormatResult(scriptResultId, jobId, "reel-script"),
            ]),
            pptxAdapter: new FakePptxAdapter(PptxBytesForEnvelope));

        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(
                AnalysisId,
                OrgId,
                new AssembleSelection([new ClientSelection(ClientId1, null)]),
                SourceJobId: jobId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).OrderBy(e => e).ToList();

        // Entries are ordered alphabetically (via OrderBy above).
        // Path order: carousel/ < script/ < story/ < manifest.json
        Assert.Equal(
            [
                "Incoders/carousel/slide_1.pptx",
                "Incoders/carousel/slide_2.pptx",
                "Incoders/script/script.md",
                "Incoders/story/story.pptx",
                "manifest.json",
            ],
            entries);
        Assert.DoesNotContain("editable-package.pptx", entries);
        Assert.DoesNotContain("Incoders/story.png", entries);
        Assert.DoesNotContain("Incoders/carousel/slide_1.png", entries);

        var flyerBytes = await ReadEntryBytesAsync(zip, "Incoders/story/story.pptx");
        Assert.Equal(PptxBytesForEnvelope(CompositionSpecJson.ParseList(EnvelopeJson("story"))[0]), flyerBytes);

        var script = await ReadEntryTextAsync(zip, "Incoders/script/script.md");
        Assert.StartsWith("# Script", script, StringComparison.Ordinal);

        var manifestJson = await ReadEntryTextAsync(zip, "manifest.json");
        Assert.Contains("\"clients\"", manifestJson);
        Assert.Contains("\"Incoders\"", manifestJson);
        Assert.Contains("\"client_folder_name\": \"Incoders\"", manifestJson);
        Assert.Contains("\"relative_path\": \"Incoders/story/story.pptx\"", manifestJson);
        Assert.Contains("\"relative_path\": \"Incoders/carousel/slide_1.pptx\"", manifestJson);
        Assert.Contains("\"relative_path\": \"Incoders/script/script.md\"", manifestJson);
        Assert.Contains("\"mime\": \"application/vnd.openxmlformats-officedocument.presentationml.presentation\"", manifestJson);
        Assert.Contains("\"mime\": \"text/markdown\"", manifestJson);
        Assert.Contains("publication", manifestJson);
        Assert.Contains("Caption for the client account.", manifestJson);
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

    // ─── B-3 (PKG-02) — Assembled folder uses client display name, not GUID ──

    [Fact]
    public async Task Handle_WithClientRepository_FolderUsesDisplayNameNotGuid()
    {
        // Given client with display name "Acme Corp" and known GUID
        var resultId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/story.png", "story.png"),
        };
        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/story.png"] = "story-bytes"u8.ToArray(),
        };

        var client = Client.Restore(
            new ClientId(ClientId1), OrgId, "Acme Corp",
            null, true, DateTime.UtcNow, DateTime.UtcNow);
        var clientRepo = new FakeClientRepository([client]);

        var handler = BuildHandler(
            files,
            rows,
            OrgId,
            AnalysisId,
            clientRepo,
            formatResultRepository: new FakeFormatResultRepository([
                MakeFormatResult(resultId, jobId, "story", EnvelopeJson("story")),
            ]),
            pptxAdapter: new FakePptxAdapter(PptxBytesForEnvelope));

        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection, SourceJobId: jobId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        // Folder must be display name, NOT the GUID
        Assert.Contains("Acme Corp/story/story.pptx", entries);
        Assert.DoesNotContain(entries, e => e.StartsWith($"{ClientId1}/"));
    }

    [Fact]
    public async Task Handle_WithClientRepository_ReservedCharsInNameAreSanitized()
    {
        // Given client with display name containing reserved chars (PKG-02 same sanitizer as PKG-01)
        var resultId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/story.png", "story.png"),
        };
        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/story.png"] = "bytes"u8.ToArray(),
        };

        var client = Client.Restore(
            new ClientId(ClientId1), OrgId, "Client:Name/Test?",
            null, true, DateTime.UtcNow, DateTime.UtcNow);
        var clientRepo = new FakeClientRepository([client]);

        var handler = BuildHandler(
            files,
            rows,
            OrgId,
            AnalysisId,
            clientRepo,
            formatResultRepository: new FakeFormatResultRepository([
                MakeFormatResult(resultId, jobId, "story", EnvelopeJson("story")),
            ]),
            pptxAdapter: new FakePptxAdapter(PptxBytesForEnvelope));

        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection, SourceJobId: jobId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        // : / ? stripped; no slug dashes added
        Assert.Contains("ClientNameTest/story/story.pptx", entries);
    }

    [Fact]
    public async Task Handle_UnknownClient_FallsBackToGuidFolder()
    {
        // Given a client GUID with no matching client record → fallback to GUID string
        var resultId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var rows = new List<FormatAssetRow>
        {
            MakeRow(resultId, 0, $"{OrgId}/{ClientId1}/{AnalysisId}/story.png", "story.png"),
        };
        var files = new Dictionary<string, byte[]>
        {
            [$"{OrgId}/{ClientId1}/{AnalysisId}/story.png"] = "bytes"u8.ToArray(),
        };

        // Empty client repo — no client record for ClientId1
        var clientRepo = new FakeClientRepository([]);

        var handler = BuildHandler(
            files,
            rows,
            OrgId,
            AnalysisId,
            clientRepo,
            formatResultRepository: new FakeFormatResultRepository([
                MakeFormatResult(resultId, jobId, "story", EnvelopeJson("story")),
            ]),
            pptxAdapter: new FakePptxAdapter(PptxBytesForEnvelope));

        var selection = new AssembleSelection([new ClientSelection(ClientId1, null)]);
        var result = await handler.Handle(
            new AssembleGeneratedPackageQuery(AnalysisId, OrgId, selection, SourceJobId: jobId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var zip = new ZipArchive(result.Value.ZipStream, ZipArchiveMode.Read);
        var entries = zip.Entries.Select(e => e.FullName).ToList();

        // Fallback: folder is the GUID string when client not found
        Assert.Contains($"{ClientId1}/story/story.pptx", entries);
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

file sealed class FakeFormatResultRepository(IReadOnlyList<ContentGenerationFormatResult> results)
    : IContentGenerationFormatResultRepository
{
    public Task<ContentGenerationFormatResult> UpsertAsync(ContentGenerationFormatResult result, CancellationToken ct) =>
        Task.FromResult(result);

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByJobAsync(Guid jobId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>(
            results.Where(result => result.JobId == jobId).ToList());

    public Task<IReadOnlyList<ContentGenerationFormatResult>> ListByIdsAsync(
        IReadOnlySet<Guid> resultIds,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ContentGenerationFormatResult>>(
            results.Where(result => resultIds.Contains(result.Id)).ToList());
}

file sealed class FakePptxAdapter : IPptxExportAdapter
{
    private readonly Func<CompositionSpecEnvelope, byte[]> _buildOne;
    private readonly Func<IReadOnlyList<CompositionSpecEnvelope>, byte[]> _buildMany;

    public FakePptxAdapter(byte[] pptxBytes)
        : this(_ => pptxBytes, _ => pptxBytes)
    {
    }

    public FakePptxAdapter(
        Func<CompositionSpecEnvelope, byte[]> buildOne,
        Func<IReadOnlyList<CompositionSpecEnvelope>, byte[]>? buildMany = null)
    {
        _buildOne = buildOne;
        _buildMany = buildMany ?? (envelopes => Encoding.UTF8.GetBytes(
            string.Join("\n", envelopes.Select(envelope => $"pptx:{envelope.Markup}"))));
    }

    public Task<Result<byte[]>> BuildAsync(CompositionSpecEnvelope envelope, CancellationToken ct) =>
        Task.FromResult(Result.Success(_buildOne(envelope)));

    public Task<Result<byte[]>> BuildManyAsync(IReadOnlyList<CompositionSpecEnvelope> envelopes, CancellationToken ct) =>
        Task.FromResult(Result.Success(_buildMany(envelopes)));
}

file sealed class FakeClientRepository(IReadOnlyList<Client> clients) : ISocialClientRepository
{
    public Task<Client?> GetByIdAsync(ClientId id, Guid organizationId, CancellationToken cancellationToken = default)
    {
        var client = clients.FirstOrDefault(c => c.Id == id && c.OrganizationId == organizationId);
        return Task.FromResult(client);
    }

    public Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(SocialClientQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<(IReadOnlyList<Client>, int)>((clients, clients.Count));

    public Task AddAsync(Client client, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void Update(Client client) { }
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
