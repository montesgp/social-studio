using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentGeneration.PackageListing;

// ─── Request / Response types ──────────────────────────────────────────────────

/// <summary>
/// Returns the per-client package tree for a given analysis, sourced from the DB
/// (<c>content_generation_format_assets</c> rows grouped by client).
/// DB is the authoritative source; this query does NOT make live storage round-trips.
/// Cross-org / unknown analysis → NotFound.
/// </summary>
public sealed record ListAnalysisPackagesQuery(
    Guid AnalysisId,
    Guid OrganizationId) : IRequest<Result<AnalysisPackagesResponse>>;

/// <summary>Top-level response: per-client list.</summary>
public sealed record AnalysisPackagesResponse(IReadOnlyList<ClientPackageEntry> Clients);

/// <summary>One client's accumulated formats for this analysis.</summary>
public sealed record ClientPackageEntry(
    Guid ClientId,
    IReadOnlyList<FormatPackageEntry> Formats);

/// <summary>
/// One format's assets for a client.
/// For carousel, <see cref="Assets"/> has N entries (one per slide).
/// For all other formats, <see cref="Assets"/> has exactly one entry.
/// </summary>
public sealed record FormatPackageEntry(
    string FormatKey,
    string Filename,
    string Mime,
    IReadOnlyList<AssetPackageEntry> Assets);

/// <summary>One stored asset entry (maps to one DB row).</summary>
public sealed record AssetPackageEntry(
    int Index,
    string Filename,
    string StoragePath,
    long SizeBytes);

// ─── Handler ──────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="ListAnalysisPackagesQuery"/>.
/// <para>
/// Algorithm:
/// 1. Tenant-scope: verify the analysis belongs to <c>OrganizationId</c>.
/// 2. Load all asset rows via <see cref="IContentGenerationFormatAssetRepository.ListByAnalysisClientAsync"/>.
/// 3. Group rows by clientId (extracted from storage path segment 1), then by formatKey
///    (derived from the filename: "carousel/slide_N.png" → "carousel"; "flyer.png" → "flyer").
/// 4. Within each formatKey group, collect all <see cref="AssetPackageEntry"/> ordered by AssetIndex.
/// 5. Return the tree as <see cref="AnalysisPackagesResponse"/>.
/// </para>
/// </summary>
public sealed class ListAnalysisPackagesQueryHandler
    : IRequestHandler<ListAnalysisPackagesQuery, Result<AnalysisPackagesResponse>>
{
    private readonly IContentGenerationFormatAssetRepository _assetRepository;
    private readonly ISingleContentAnalysisRepository _analysisRepository;

    public ListAnalysisPackagesQueryHandler(
        IContentGenerationFormatAssetRepository assetRepository,
        ISingleContentAnalysisRepository analysisRepository)
    {
        ArgumentNullException.ThrowIfNull(assetRepository);
        ArgumentNullException.ThrowIfNull(analysisRepository);

        _assetRepository = assetRepository;
        _analysisRepository = analysisRepository;
    }

    public async Task<Result<AnalysisPackagesResponse>> Handle(
        ListAnalysisPackagesQuery query,
        CancellationToken cancellationToken)
    {
        // 1. Tenant-scope: verify the analysis belongs to the requesting organization
        var analysis = await _analysisRepository.GetByIdAsync(
            query.AnalysisId,
            query.OrganizationId,
            cancellationToken);

        if (analysis is null)
        {
            return Result.Failure<AnalysisPackagesResponse>(
                ContentGenerationErrors.AnalysisNotFound);
        }

        // 2. Load all per-asset rows for this org/analysis
        var allRows = await _assetRepository.ListByAnalysisClientAsync(
            query.OrganizationId,
            query.AnalysisId,
            cancellationToken);

        // 3. Group by clientId, then by formatKey
        // Storage path layout: {orgId}/{clientId}/{analysisId}/[carousel/]<filename>
        var clientGroups = new Dictionary<Guid, Dictionary<string, List<FormatAssetRow>>>();

        foreach (var row in allRows)
        {
            if (!TryExtractClientId(row.StoragePath, query.OrganizationId, query.AnalysisId, out var clientId))
                continue;

            var formatKey = DeriveFormatKey(row.Filename);

            if (!clientGroups.TryGetValue(clientId, out var formatMap))
            {
                formatMap = new Dictionary<string, List<FormatAssetRow>>(StringComparer.OrdinalIgnoreCase);
                clientGroups[clientId] = formatMap;
            }

            if (!formatMap.TryGetValue(formatKey, out var rowList))
            {
                rowList = [];
                formatMap[formatKey] = rowList;
            }

            rowList.Add(row);
        }

        // 4. Build the response tree
        var clientEntries = clientGroups
            .Select(kvClient =>
            {
                var formatEntries = kvClient.Value
                    .Select(kvFormat =>
                    {
                        var orderedRows = kvFormat.Value.OrderBy(r => r.AssetIndex).ToList();
                        var firstRow = orderedRows[0];

                        // Representative filename for the format: for carousel → "carousel/"
                        // (the folder); for others → the single asset filename.
                        var representativeFilename = kvFormat.Key == "carousel"
                            ? "carousel/"
                            : firstRow.Filename;

                        var assets = orderedRows
                            .Select(r => new AssetPackageEntry(
                                Index: r.AssetIndex,
                                Filename: r.Filename,
                                StoragePath: r.StoragePath,
                                SizeBytes: r.SizeBytes))
                            .ToList();

                        return new FormatPackageEntry(
                            FormatKey: kvFormat.Key,
                            Filename: representativeFilename,
                            Mime: firstRow.MimeType,
                            Assets: assets);
                    })
                    .ToList<FormatPackageEntry>();

                return new ClientPackageEntry(
                    ClientId: kvClient.Key,
                    Formats: formatEntries);
            })
            .ToList<ClientPackageEntry>();

        return Result.Success(new AnalysisPackagesResponse(clientEntries));
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the clientId GUID from a storage path of the form
    /// <c>{orgId}/{clientId}/{analysisId}/...</c>.
    /// Returns false if the path doesn't conform to the expected layout.
    /// </summary>
    private static bool TryExtractClientId(
        string storagePath,
        Guid organizationId,
        Guid analysisId,
        out Guid clientId)
    {
        clientId = Guid.Empty;

        var segments = storagePath.Split('/');
        if (segments.Length < 4)
            return false;

        if (!Guid.TryParse(segments[0], out var pathOrgId) || pathOrgId != organizationId)
            return false;

        if (!Guid.TryParse(segments[1], out var pathClientId))
            return false;

        if (!Guid.TryParse(segments[2], out var pathAnalysisId) || pathAnalysisId != analysisId)
            return false;

        clientId = pathClientId;
        return true;
    }

    /// <summary>
    /// Derives a format key from a row's <c>Filename</c>.
    /// Carousel slides have filename like <c>slide_1.png</c> but live under a
    /// <c>carousel/</c> directory, which is reflected in their StoragePath.
    /// We detect carousel by checking if the storage path segment after analysisId is "carousel".
    /// For all other formats the filename stem IS the format key (e.g. <c>flyer.png</c> → <c>flyer</c>).
    /// </summary>
    private static string DeriveFormatKey(string filename)
    {
        // filename for carousel slides is "slide_N.png" (the Filename field stores only the leaf name).
        // We rely on the FormatResultId grouping + AssetIndex, but we need to distinguish
        // carousel by the fact that rows share a FormatResultId. However, at this grouping stage
        // we don't have that context — we derive from the storage path indirectly.
        //
        // Simpler: the Filename stored in the DB for non-carousel assets is the full relative filename
        // set by the pipeline (e.g. "flyer.png", "reel-script.txt"), while carousel slide rows
        // have Filename = "slide_N.png". We detect carousel by the naming pattern.
        if (System.Text.RegularExpressions.Regex.IsMatch(filename, @"^slide_\d+\.png$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return "carousel";
        }

        // For all others: strip extension from the leaf filename to get the format key.
        // E.g. "flyer.png" → "flyer", "reel-script.txt" → "reel-script"
        var dotIndex = filename.LastIndexOf('.');
        return dotIndex > 0 ? filename[..dotIndex] : filename;
    }
}
