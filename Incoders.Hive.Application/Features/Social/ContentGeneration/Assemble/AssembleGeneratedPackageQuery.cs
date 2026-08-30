using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Packaging;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.ContentFormats;
using Incoders.Hive.Domain.Social.ContentGeneration;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.Assemble;

// ─── Request / Response types ──────────────────────────────────────────────────

/// <summary>
/// Describes which clients (and optionally which format keys) to include in the assembled ZIP.
/// A null <see cref="ClientSelection.FormatKeys"/> means "all stored assets for that client".
/// </summary>
public sealed record ClientSelection(
    Guid ClientId,
    IReadOnlyList<string>? FormatKeys);

/// <summary>Selection envelope for <see cref="AssembleGeneratedPackageQuery"/>.</summary>
public sealed record AssembleSelection(IReadOnlyList<ClientSelection> Clients);

/// <summary>
/// Assemble-on-demand query: composes a ZIP from currently-stored per-asset rows for the
/// given <paramref name="AnalysisId"/> and <paramref name="Selection"/>.
/// </summary>
public sealed record AssembleGeneratedPackageQuery(
    Guid AnalysisId,
    Guid OrganizationId,
    AssembleSelection Selection,
    Guid? SourceJobId = null,
    Guid? UserId = null) : IRequest<Result<AssembleGeneratedPackageResponse>>;

/// <summary>
/// Response: open readable ZIP stream + suggested download filename.
/// Caller is responsible for disposing <see cref="ZipStream"/>.
/// </summary>
public sealed record AssembleGeneratedPackageResponse(
    Stream ZipStream,
    string DownloadFilename);

// ─── Handler ──────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="AssembleGeneratedPackageQuery"/>.
/// <para>
/// Algorithm:
/// 1. Tenant-scope: verify the analysis belongs to <c>OrganizationId</c> via
///    <see cref="ISingleContentAnalysisRepository.GetByIdAsync"/> — cross-tenant → NotFound.
/// 2. Load all asset rows for the analysis/org from the repository.
/// 3. For each selected client (filtered by <see cref="ClientSelection.FormatKeys"/> when provided),
///    materialize supported format deliverables under <c>{client-folder}/{format}/...</c>.
///    Visual formats (<c>story</c>, <c>feed-post</c>, <c>carousel</c>) are exported as editable PPTX from persisted
///    composition specs; script is copied as Markdown from durable storage.
/// 4. Write <c>manifest.json</c> at the ZIP root using the client-first manifest contract.
/// 5. Return the seekable MemoryStream + Content-Disposition filename.
/// </para>
/// </summary>
public sealed class AssembleGeneratedPackageQueryHandler
    : IRequestHandler<AssembleGeneratedPackageQuery, Result<AssembleGeneratedPackageResponse>>
{
    private const string PptxMime = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    private const string MarkdownMime = "text/markdown";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IGeneratedAssetStorage _assetStorage;
    private readonly IContentGenerationFormatAssetRepository _assetRepository;
    private readonly ISingleContentAnalysisRepository _analysisRepository;
    private readonly ISocialClientRepository _clientRepository;
    private readonly IContentGenerationFormatResultRepository? _formatResultRepository;
    private readonly IPptxExportAdapter? _pptxAdapter;
    private readonly IGeneratedPackageRepository? _packageRepository;
    private readonly IClock? _clock;

    public AssembleGeneratedPackageQueryHandler(
        IGeneratedAssetStorage assetStorage,
        IContentGenerationFormatAssetRepository assetRepository,
        ISingleContentAnalysisRepository analysisRepository,
        ISocialClientRepository clientRepository,
        IContentGenerationFormatResultRepository? formatResultRepository = null,
        IPptxExportAdapter? pptxAdapter = null,
        IGeneratedPackageRepository? packageRepository = null,
        IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(assetStorage);
        ArgumentNullException.ThrowIfNull(assetRepository);
        ArgumentNullException.ThrowIfNull(analysisRepository);
        ArgumentNullException.ThrowIfNull(clientRepository);

        _assetStorage = assetStorage;
        _assetRepository = assetRepository;
        _analysisRepository = analysisRepository;
        _clientRepository = clientRepository;
        _formatResultRepository = formatResultRepository;
        _pptxAdapter = pptxAdapter;
        _packageRepository = packageRepository;
        _clock = clock;
    }

    public async Task<Result<AssembleGeneratedPackageResponse>> Handle(
        AssembleGeneratedPackageQuery query,
        CancellationToken cancellationToken)
    {
        // 1. Tenant-scope: verify analysis belongs to the requesting organization
        var analysis = await _analysisRepository.GetByIdAsync(
            query.AnalysisId,
            query.OrganizationId,
            cancellationToken);

        if (analysis is null)
        {
            return Result.Failure<AssembleGeneratedPackageResponse>(
                ContentGenerationErrors.AnalysisNotFound);
        }

        // 2. Load all per-asset rows for this org/analysis (all clients combined)
        var allRows = await _assetRepository.ListByAnalysisClientAsync(
            query.OrganizationId,
            query.AnalysisId,
            cancellationToken);

        // 2b. Keep only the latest format-result version per (client, format) so assembled
        // ZIPs never include stale assets from previous generations/regenerations.
        var latestRows = SelectLatestRowsPerClientFormat(
            allRows,
            query.OrganizationId,
            query.AnalysisId);

        // 2c. Resolve display names for all distinct client IDs (deduped via Dictionary).
        // Each lookup is scoped to this organization; null/empty name → GUID fallback.
        var clientFolders = await ResolveClientFoldersAsync(
            latestRows.Select(r => r.ClientId).Distinct(),
            query.OrganizationId,
            cancellationToken);

        // 3. Determine the effective client set from the selection.
        // A single entry with ClientId == Guid.Empty is the wildcard sentinel meaning "all clients".
        var isWildcard = query.Selection.Clients.Count == 1
            && query.Selection.Clients[0].ClientId == Guid.Empty;

        var selectedClientIds = isWildcard
            ? null  // null = include all clients
            : query.Selection.Clients.Select(c => c.ClientId).ToHashSet();

        // Build a quick lookup: clientId → allowed format keys (null = all)
        var clientFormatFilter = isWildcard
            ? new Dictionary<Guid, HashSet<string>?>()
            : query.Selection.Clients
                .ToDictionary(
                    c => c.ClientId,
                    c => c.FormatKeys is { Count: > 0 }
                        ? new HashSet<string>(c.FormatKeys, StringComparer.OrdinalIgnoreCase)
                        : null);

        var selectedRows = latestRows
            .Where(parsed => selectedClientIds is null || selectedClientIds.Contains(parsed.ClientId))
            .Where(parsed =>
            {
                if (!clientFormatFilter.TryGetValue(parsed.ClientId, out var allowedFormats)
                    || allowedFormats is null)
                {
                    return true;
                }

                return allowedFormats.Contains(parsed.FormatKey);
            })
            .ToList();

        var selectedFormatResults = await ResolveSelectedFormatResultsAsync(
            query.SourceJobId,
            selectedRows,
            cancellationToken);

        // 4. Build the ZIP in memory
        // For large carousels this is bounded by the carousel size — the design calls
        // for MemoryStream (temp-file approach is an INC-4 optimization deferred to INC-5+).
        var memStream = new MemoryStream();

        using (var archive = new ZipArchive(memStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var writtenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var manifest = await WritePackageEntriesAsync(
                archive,
                selectedRows,
                selectedFormatResults,
                clientFolders,
                writtenEntries,
                cancellationToken);

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using (var manifestStream = manifestEntry.Open())
            {
                var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
                await manifestStream.WriteAsync(manifestBytes, cancellationToken);
            }
        }

        memStream.Position = 0;

        if (query.SourceJobId is { } sourceJobId
            && _packageRepository is not null
            && _clock is not null)
        {
            await _packageRepository.MarkDownloadedAsync(
                sourceJobId,
                query.OrganizationId,
                query.UserId,
                _clock.UtcNow,
                cancellationToken);
        }

        var filename = $"content-package-{query.AnalysisId}.zip";
        return Result.Success(new AssembleGeneratedPackageResponse(memStream, filename));
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task<IReadOnlyList<ContentGenerationFormatResult>> ResolveSelectedFormatResultsAsync(
        Guid? sourceJobId,
        IReadOnlyList<ParsedFormatAsset> selectedRows,
        CancellationToken ct)
    {
        if (_formatResultRepository is null)
        {
            return [];
        }

        if (sourceJobId is { } jobId && jobId != Guid.Empty)
        {
            return await _formatResultRepository.ListByJobAsync(jobId, ct);
        }

        var ids = selectedRows
            .Select(r => r.Row.FormatResultId)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        return await _formatResultRepository.ListByIdsAsync(ids, ct);
    }

    private async Task<AssembledPackageManifest> WritePackageEntriesAsync(
        ZipArchive archive,
        IReadOnlyList<ParsedFormatAsset> selectedRows,
        IReadOnlyList<ContentGenerationFormatResult> selectedFormatResults,
        IReadOnlyDictionary<Guid, string> clientFolders,
        HashSet<string> writtenEntries,
        CancellationToken ct)
    {
        var globalWarnings = new List<string>();
        var clients = new SortedDictionary<string, AssembledClientManifest>(StringComparer.OrdinalIgnoreCase);

        foreach (var clientGroup in selectedRows.GroupBy(row => row.ClientId))
        {
            var folder = clientFolders.TryGetValue(clientGroup.Key, out var folderName)
                ? folderName
                : clientGroup.Key.ToString();

            var clientWarnings = new List<string>();
            var formats = new SortedDictionary<string, AssembledFormatManifest>(StringComparer.OrdinalIgnoreCase);

            foreach (var formatGroup in clientGroup.GroupBy(row => row.FormatKey, StringComparer.OrdinalIgnoreCase))
            {
                var packageFormatKey = NormalizePackageFormatKey(formatGroup.Key);
                if (packageFormatKey is null)
                {
                    clientWarnings.Add($"Unsupported package format '{formatGroup.Key}' was skipped.");
                    continue;
                }

                // Resolve the definition for routing. Multi-asset = carousel family.
                var routingDef = FormatCatalog.ResolveForAssembly(formatGroup.Key);

                AssembledFormatManifest? formatManifest;
                if (packageFormatKey == "script")
                {
                    formatManifest = await TryWriteScriptAsync(
                        archive,
                        folder,
                        formatGroup.OrderBy(row => row.Row.AssetIndex).ToList(),
                        writtenEntries,
                        clientWarnings,
                        ct);
                }
                else if (routingDef?.IsMultiAsset == true)
                {
                    // Carousel family: story-carousel, feed-carousel, legacy carousel.
                    // storageFolder comes from the resolved definition so each key writes
                    // to its own folder (ADR-5 / SCENARIO-05/07).
                    formatManifest = await TryWriteCarouselAsync(
                        archive,
                        folder,
                        formatGroup.ToList(),
                        selectedFormatResults,
                        writtenEntries,
                        clientWarnings,
                        routingDef.StorageFolder,
                        ct);
                }
                else if (routingDef is not null && !routingDef.IsMultiAsset)
                {
                    // Single-asset visual (story, feed-post).
                    formatManifest = await TryWriteStoryAsync(
                        archive,
                        folder,
                        formatGroup.ToList(),
                        selectedFormatResults,
                        writtenEntries,
                        clientWarnings,
                        ct);
                }
                else
                {
                    formatManifest = null;
                }

                if (formatManifest is not null)
                {
                    formats[packageFormatKey] = formatManifest;
                }
            }

            if (formats.Count == 0)
            {
                continue;
            }

            var resultIds = clientGroup
                .Select(row => row.Row.FormatResultId)
                .Where(id => id != Guid.Empty)
                .ToHashSet();

            var inferredFields = selectedFormatResults
                .Where(result => resultIds.Contains(result.Id))
                .SelectMany(result => ReadInferredFields(result.InferredFieldsJson))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var totalSize = formats.Values.Sum(format => format.SizeBytes);
            clients[folder] = new AssembledClientManifest(
                ClientFolderName: folder,
                Formats: formats,
                InferredFields: inferredFields,
                Warnings: clientWarnings,
                TotalSizeBytes: totalSize,
                HasStoredAssets: true);

            globalWarnings.AddRange(clientWarnings.Select(warning => $"{folder}: {warning}"));
        }

        return new AssembledPackageManifest(
            Clients: clients,
            TotalSizeBytes: clients.Values.Sum(client => client.TotalSizeBytes),
            Warnings: globalWarnings);
    }

    private async Task<AssembledFormatManifest?> TryWriteStoryAsync(
        ZipArchive archive,
        string clientFolder,
        IReadOnlyList<ParsedFormatAsset> rows,
        IReadOnlyList<ContentGenerationFormatResult> selectedFormatResults,
        HashSet<string> writtenEntries,
        List<string> warnings,
        CancellationToken ct)
    {
        var result = ResolveFormatResult(rows, selectedFormatResults);
        var envelopes = CompositionSpecJson.ParseActiveList(result?.CompositionSpecJson);
        if (_pptxAdapter is null || result is null || envelopes.Count == 0)
        {
            warnings.Add("story skipped because no editable composition spec was available.");
            return null;
        }

        var buildResult = envelopes.Count == 1
            ? await _pptxAdapter.BuildAsync(envelopes[0], ct)
            : await _pptxAdapter.BuildManyAsync(envelopes, ct);

        if (buildResult.IsFailure)
        {
            warnings.Add($"story skipped because PPTX export failed: {buildResult.Error.Code}.");
            return null;
        }

        var formatKey = result.FormatKey ?? "story";
        var file = await WriteBytesEntryAsync(
            archive,
            writtenEntries,
            $"{clientFolder}/{formatKey}/{formatKey}.pptx",
            buildResult.Value,
            PptxMime,
            ct);

        return file is null
            ? null
            : BuildFormatManifest(formatKey, file, ResolvePublicationMetadata(formatKey, result.OutputJson));
    }

    /// <summary>
    /// Writes carousel-family slides to the ZIP as editable PPTX files.
    /// </summary>
    /// <param name="storageFolder">
    /// The folder token used in ZIP paths (e.g. "story-carousel", "feed-carousel", "carousel").
    /// Sourced from <see cref="FormatDefinition.StorageFolder"/> via ResolveForAssembly (ADR-5).
    /// New keys write under their own folder; legacy "carousel" rows keep the historical layout.
    /// </param>
    private async Task<AssembledFormatManifest?> TryWriteCarouselAsync(
        ZipArchive archive,
        string clientFolder,
        IReadOnlyList<ParsedFormatAsset> rows,
        IReadOnlyList<ContentGenerationFormatResult> selectedFormatResults,
        HashSet<string> writtenEntries,
        List<string> warnings,
        string storageFolder,
        CancellationToken ct)
    {
        var result = ResolveFormatResult(rows, selectedFormatResults);
        var envelopes = CompositionSpecJson.ParseActiveList(result?.CompositionSpecJson);
        if (_pptxAdapter is null || result is null || envelopes.Count == 0)
        {
            warnings.Add($"{storageFolder} skipped because no editable composition spec was available.");
            return null;
        }

        var files = new List<AssembledFileManifest>();
        for (var i = 0; i < envelopes.Count; i++)
        {
            var buildResult = await _pptxAdapter.BuildAsync(envelopes[i], ct);
            if (buildResult.IsFailure)
            {
                warnings.Add($"{storageFolder} slide {i + 1} skipped because PPTX export failed: {buildResult.Error.Code}.");
                continue;
            }

            var file = await WriteBytesEntryAsync(
                archive,
                writtenEntries,
                $"{clientFolder}/{storageFolder}/slide_{i + 1}.pptx",
                buildResult.Value,
                PptxMime,
                ct);

            if (file is not null)
            {
                files.Add(file);
            }
        }

        if (files.Count == 0)
        {
            return null;
        }

        return BuildFormatManifest(
            storageFolder,
            files,
            ResolvePublicationMetadata(storageFolder, result.OutputJson),
            aggregateFilename: $"{storageFolder}/",
            aggregateRelativePath: $"{clientFolder}/{storageFolder}/");
    }

    private async Task<AssembledFormatManifest?> TryWriteScriptAsync(
        ZipArchive archive,
        string clientFolder,
        IReadOnlyList<ParsedFormatAsset> rows,
        HashSet<string> writtenEntries,
        List<string> warnings,
        CancellationToken ct)
    {
        foreach (var row in rows)
        {
            var openResult = await _assetStorage.OpenAsync(row.Row.StoragePath, ct);
            if (openResult.IsFailure)
            {
                warnings.Add($"script source asset '{row.Row.Filename}' was missing and was skipped.");
                continue;
            }

            await using var sourceStream = openResult.Value.Content;
            using var memory = new MemoryStream();
            await sourceStream.CopyToAsync(memory, ct);

            var file = await WriteBytesEntryAsync(
                archive,
                writtenEntries,
                $"{clientFolder}/script/script.md",
                memory.ToArray(),
                MarkdownMime,
                ct);

            return file is null
                ? null
                : BuildFormatManifest("script", file, publication: null);
        }

        return null;
    }

    private static ContentGenerationFormatResult? ResolveFormatResult(
        IReadOnlyList<ParsedFormatAsset> rows,
        IReadOnlyList<ContentGenerationFormatResult> selectedFormatResults)
    {
        var rowResultIds = rows
            .Select(row => row.Row.FormatResultId)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        var fromRowId = selectedFormatResults
            .Where(result => rowResultIds.Contains(result.Id))
            .OrderByDescending(result => result.UpdatedAt)
            .FirstOrDefault();

        if (fromRowId is not null)
        {
            return fromRowId;
        }

        var rawFormatKey = rows.FirstOrDefault()?.FormatKey;
        return rawFormatKey is null
            ? null
            : selectedFormatResults
                .Where(result => string.Equals(result.FormatKey, rawFormatKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(result => result.UpdatedAt)
                .FirstOrDefault();
    }

    private static async Task<AssembledFileManifest?> WriteBytesEntryAsync(
        ZipArchive archive,
        HashSet<string> writtenEntries,
        string relativePath,
        byte[] bytes,
        string mime,
        CancellationToken ct)
    {
        if (!writtenEntries.Add(relativePath))
        {
            return null;
        }

        var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(bytes, ct);

        return new AssembledFileManifest(
            Filename: Path.GetFileName(relativePath),
            RelativePath: relativePath,
            Mime: mime,
            SizeBytes: bytes.LongLength,
            ContentHash: ComputeStablePseudoHash(bytes));
    }

    private static AssembledFormatManifest BuildFormatManifest(
        string formatKey,
        AssembledFileManifest file,
        PublicationMetadata? publication) =>
        BuildFormatManifest(formatKey, [file], publication, file.Filename, file.RelativePath);

    private static AssembledFormatManifest BuildFormatManifest(
        string formatKey,
        IReadOnlyList<AssembledFileManifest> files,
        PublicationMetadata? publication,
        string aggregateFilename,
        string aggregateRelativePath)
    {
        var size = files.Sum(file => file.SizeBytes);
        return new AssembledFormatManifest(
            FormatKey: formatKey,
            Mime: files.Count == 1 ? files[0].Mime : PptxMime,
            Filename: aggregateFilename,
            RelativePath: aggregateRelativePath,
            SizeBytes: size,
            ContentHash: ComputeCombinedHash(files),
            Publication: publication,
            Files: files,
            Assets: files);
    }

    /// <summary>
    /// Resolves a stored format key to the routing token used by the assembly switch.
    /// Uses <see cref="FormatCatalog.ResolveForAssembly"/> so both canonical keys (story-carousel,
    /// feed-carousel, story, feed-post, reel-script) and the legacy "carousel" alias resolve.
    /// Returns null for unknown keys that cannot be assembled.
    /// </summary>
    private static string? NormalizePackageFormatKey(string formatKey)
    {
        // Special-case script/reel-script before catalog lookup: the catalog key is
        // "reel-script" but the assembly routing uses the shorter token "script".
        if (string.Equals(formatKey, "script", StringComparison.OrdinalIgnoreCase)
            || string.Equals(formatKey, "reel-script", StringComparison.OrdinalIgnoreCase))
        {
            return "script";
        }

        // Catalog + alias aware lookup (canonical keys AND legacy "carousel" alias).
        var def = FormatCatalog.ResolveForAssembly(formatKey);
        if (def is null)
        {
            return null;
        }

        // Multi-asset carousel family (story-carousel, feed-carousel, legacy carousel):
        // route to the carousel writer; preserve the original format key so the writer
        // uses the correct storageFolder.
        if (def.IsMultiAsset)
        {
            return formatKey; // caller passes storageFolder = def.StorageFolder
        }

        // Single-asset visual formats (story, feed-post).
        return def.FormatKey;
    }

    private static PublicationMetadata? ResolvePublicationMetadata(string formatKey, string? outputJson)
    {
        // Publication metadata is only available for visual formats (catalog-driven check).
        // Falls back to the legacy "carousel" alias via ResolveForAssembly so legacy rows still get
        // publication metadata if present.
        var def = FormatCatalog.ResolveForAssembly(formatKey);
        if (def is null || !def.IsVisual)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(outputJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(outputJson);
            if (!document.RootElement.TryGetProperty("publicationDescription", out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var description = property.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(description)
                ? null
                : new PublicationMetadata(description);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadInferredFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ComputeStablePseudoHash(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeCombinedHash(IReadOnlyList<AssembledFileManifest> files)
    {
        if (files.Count == 1)
        {
            return files[0].ContentHash;
        }

        var combined = string.Join(
            "|",
            files.Select(file => $"{file.RelativePath}:{file.ContentHash}"));
        return ComputeStablePseudoHash(Encoding.UTF8.GetBytes(combined));
    }

    /// <summary>
    /// Resolves display-name folder segments for a set of client GUIDs within the given organization.
    /// Each distinct clientId is looked up at most once (deduplication via <see cref="Dictionary{K,V}"/>).
    /// When a client record is not found, the entry is omitted — callers fall back to the GUID string.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ResolveClientFoldersAsync(
        IEnumerable<Guid> clientIds,
        Guid organizationId,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var clientId in clientIds)
        {
            if (result.ContainsKey(clientId))
                continue;

            var client = await _clientRepository.GetByIdAsync(
                new ClientId(clientId),
                organizationId,
                ct);

            if (client is not null)
            {
                var folder = GeneratedPackageBuilder.NormalizeFolderName(client.Name);
                if (!string.IsNullOrEmpty(folder))
                    result[clientId] = folder;
                else
                    result[clientId] = clientId.ToString(); // sanitized-to-empty edge case
            }
            // client is null → not added → caller falls back to GUID
        }
        return result;
    }

    /// <summary>
    /// Attempts to extract the clientId GUID and relative asset path from a storage path of the form
    /// <c>{orgId}/{clientId}/{analysisId}/{relativePath}</c>.
    /// Returns false if the path doesn't conform to the expected layout.
    /// </summary>
    private static bool TryExtractAssetPath(
        string storagePath,
        Guid organizationId,
        Guid analysisId,
        out Guid clientId,
        out string relativeAssetPath)
    {
        clientId = Guid.Empty;
        relativeAssetPath = string.Empty;

        // Expected segments: [orgId, clientId, analysisId, ...rest]
        var segments = storagePath.Split('/');
        if (segments.Length < 4)
            return false;

        if (!Guid.TryParse(segments[0], out var pathOrgId) || pathOrgId != organizationId)
            return false;

        if (!Guid.TryParse(segments[1], out var pathClientId))
            return false;

        if (!Guid.TryParse(segments[2], out var pathAnalysisId) || pathAnalysisId != analysisId)
            return false;

        relativeAssetPath = string.Join("/", segments.Skip(3)).Trim('/');
        if (string.IsNullOrWhiteSpace(relativeAssetPath))
            return false;

        clientId = pathClientId;
        return true;
    }

    /// <summary>
    /// Derives a format key from a filename. For carousel slides the filename is
    /// <c>story-carousel/slide_N.png</c> → format key is <c>story-carousel</c>.
    /// For others it's the filename stem, e.g. <c>story.png</c> → <c>story</c>.
    /// </summary>
    private static string DeriveFormatKey(string filename)
    {
        // carousel/slide_N.png → first segment is the format key
        var firstSegment = filename.Split('/')[0];
        // strip extension
        var dotIndex = firstSegment.LastIndexOf('.');
        return dotIndex > 0 ? firstSegment[..dotIndex] : firstSegment;
    }

    private static IReadOnlyList<ParsedFormatAsset> SelectLatestRowsPerClientFormat(
        IReadOnlyList<FormatAssetRow> allRows,
        Guid organizationId,
        Guid analysisId)
    {
        var parsedRows = allRows
            .Select(row =>
            {
                var ok = TryExtractAssetPath(
                    row.StoragePath,
                    organizationId,
                    analysisId,
                    out var clientId,
                    out var relativeAssetPath);

                if (!ok)
                {
                    return null;
                }

                return new ParsedFormatAsset(
                    ClientId: clientId,
                    RelativeAssetPath: relativeAssetPath,
                    FormatKey: DeriveFormatKey(relativeAssetPath),
                    Row: row);
            })
            .Where(row => row is not null)
            .Select(row => row!)
            .ToList();

        return parsedRows
            .GroupBy(r => new { r.ClientId, r.FormatKey })
            .SelectMany(group =>
            {
                var latestVersion = group
                    .GroupBy(r => r.Row.FormatResultId)
                    .OrderByDescending(version => version.Max(v => v.Row.JobCreatedAtUtc))
                    .ThenByDescending(version => version.Max(v => v.Row.UpdatedAt))
                    .ThenByDescending(version => version.Key)
                    .First();

                return latestVersion;
            })
            .ToList();
    }

    private sealed record ParsedFormatAsset(
        Guid ClientId,
        string RelativeAssetPath,
        string FormatKey,
        FormatAssetRow Row);

    private sealed record AssembledPackageManifest(
        IReadOnlyDictionary<string, AssembledClientManifest> Clients,
        long TotalSizeBytes,
        IReadOnlyList<string> Warnings);

    private sealed record AssembledClientManifest(
        string ClientFolderName,
        IReadOnlyDictionary<string, AssembledFormatManifest> Formats,
        IReadOnlyList<string> InferredFields,
        IReadOnlyList<string> Warnings,
        long TotalSizeBytes,
        bool HasStoredAssets);

    private sealed record AssembledFormatManifest(
        string FormatKey,
        string Mime,
        string Filename,
        string RelativePath,
        long SizeBytes,
        string ContentHash,
        PublicationMetadata? Publication,
        IReadOnlyList<AssembledFileManifest> Files,
        IReadOnlyList<AssembledFileManifest> Assets);

    private sealed record AssembledFileManifest(
        string Filename,
        string RelativePath,
        string Mime,
        long SizeBytes,
        string ContentHash);
}
