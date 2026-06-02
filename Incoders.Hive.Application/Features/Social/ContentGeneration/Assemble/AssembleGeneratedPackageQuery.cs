using System.IO.Compression;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Domain.Common;
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
    AssembleSelection Selection) : IRequest<Result<AssembleGeneratedPackageResponse>>;

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
///    open each asset from <see cref="IGeneratedAssetStorage.OpenAsync"/>.
///    - 404 (NotFound) → skip that entry silently with a warning entry name; never abort the whole ZIP.
/// 4. Write a ZIP where each entry is laid out as <c>{clientId}/{assetFilename}</c>.
///    Carousel slides keep their <c>carousel/slide_N.png</c> sub-path, so the final
///    ZIP entry is <c>{clientId}/carousel/slide_N.png</c>.
/// 5. Return the seekable MemoryStream + Content-Disposition filename.
/// </para>
/// </summary>
public sealed class AssembleGeneratedPackageQueryHandler
    : IRequestHandler<AssembleGeneratedPackageQuery, Result<AssembleGeneratedPackageResponse>>
{
    private readonly IGeneratedAssetStorage _assetStorage;
    private readonly IContentGenerationFormatAssetRepository _assetRepository;
    private readonly ISingleContentAnalysisRepository _analysisRepository;

    public AssembleGeneratedPackageQueryHandler(
        IGeneratedAssetStorage assetStorage,
        IContentGenerationFormatAssetRepository assetRepository,
        ISingleContentAnalysisRepository analysisRepository)
    {
        ArgumentNullException.ThrowIfNull(assetStorage);
        ArgumentNullException.ThrowIfNull(assetRepository);
        ArgumentNullException.ThrowIfNull(analysisRepository);

        _assetStorage = assetStorage;
        _assetRepository = assetRepository;
        _analysisRepository = analysisRepository;
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

        // 4. Build the ZIP in memory
        // For large carousels this is bounded by the carousel size — the design calls
        // for MemoryStream (temp-file approach is an INC-4 optimization deferred to INC-5+).
        var memStream = new MemoryStream();

        using (var archive = new ZipArchive(memStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var writtenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in allRows)
            {
                // Derive the clientId and relative asset path from:
                // {orgId}/{clientId}/{analysisId}/{relativePath}
                if (!TryExtractAssetPath(
                        row.StoragePath,
                        query.OrganizationId,
                        query.AnalysisId,
                        out var clientId,
                        out var relativeAssetPath))
                {
                    continue;
                }

                // Only include selected clients (null selectedClientIds = wildcard = include all)
                if (selectedClientIds is not null && !selectedClientIds.Contains(clientId))
                    continue;

                // Apply per-client format filter if provided
                if (clientFormatFilter.TryGetValue(clientId, out var allowedFormats)
                    && allowedFormats is not null)
                {
                    var formatKey = DeriveFormatKey(relativeAssetPath);
                    if (!allowedFormats.Contains(formatKey))
                        continue;
                }

                // Open asset from storage — 404 → skip silently
                var openResult = await _assetStorage.OpenAsync(row.StoragePath, cancellationToken);
                if (openResult.IsFailure)
                {
                    // Missing asset: skip without aborting the whole ZIP
                    continue;
                }

                using var assetStream = openResult.Value;

                // ZIP entry path: {clientId}/{relativeFilename}
                // The DB filename is intentionally a leaf for carousel slides; the storage path
                // is authoritative for preserving subfolders such as carousel/slide_1.png.
                var entryName = $"{clientId}/{relativeAssetPath}";
                if (!writtenEntries.Add(entryName))
                {
                    continue;
                }

                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                await using var entryStream = entry.Open();
                await assetStream.Content.CopyToAsync(entryStream, cancellationToken);
            }
        }

        memStream.Position = 0;

        var filename = $"content-package-{query.AnalysisId}.zip";
        return Result.Success(new AssembleGeneratedPackageResponse(memStream, filename));
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

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
    /// <c>carousel/slide_N.png</c> → format key is <c>carousel</c>.
    /// For others it's the filename stem, e.g. <c>flyer.png</c> → <c>flyer</c>.
    /// </summary>
    private static string DeriveFormatKey(string filename)
    {
        // carousel/slide_N.png → first segment is the format key
        var firstSegment = filename.Split('/')[0];
        // strip extension
        var dotIndex = firstSegment.LastIndexOf('.');
        return dotIndex > 0 ? firstSegment[..dotIndex] : firstSegment;
    }
}
