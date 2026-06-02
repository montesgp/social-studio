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
/// 3. Parse each storage path as <c>{orgId}/{clientId}/{analysisId}/{relativePath}</c> and derive formatKey.
/// 4. Keep only the latest format-result version per (clientId, formatKey) to avoid stale duplicates
///    from previous jobs (for example duplicate carousel slides after regeneration).
/// 5. Within each selected format, collect all <see cref="AssetPackageEntry"/> ordered by AssetIndex.
/// 6. Return the tree as <see cref="AnalysisPackagesResponse"/>.
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

        // 3. Parse storage-path metadata and derive format keys.
        var parsedRows = allRows
            .Select(row =>
            {
                var ok = TryExtractClientAndRelativePath(
                    row.StoragePath,
                    query.OrganizationId,
                    query.AnalysisId,
                    out var clientId,
                    out var relativePath);

                if (!ok)
                {
                    return null;
                }

                return new ParsedFormatAsset(
                    ClientId: clientId,
                    RelativePath: relativePath,
                    FormatKey: DeriveFormatKey(relativePath),
                    Row: row);
            })
            .Where(row => row is not null)
            .Select(row => row!)
            .ToList();

        // 4. Keep only the latest version per (client, format).
        var latestRows = SelectLatestRowsPerClientFormat(parsedRows);

        // 5. Build the response tree
        var clientEntries = latestRows
            .GroupBy(r => r.ClientId)
            .Select(kvClient =>
            {
                var formatEntries = kvClient
                    .GroupBy(r => r.FormatKey, StringComparer.OrdinalIgnoreCase)
                    .Select(kvFormat =>
                    {
                        var orderedRows = kvFormat
                            .Select(r => r.Row)
                            .OrderBy(r => r.AssetIndex)
                            .ToList();
                        var firstRow = orderedRows[0];

                        // Representative filename for the format: for carousel → "carousel/"
                        // (the folder); for others → the single asset filename.
                        var representativeFilename = string.Equals(kvFormat.Key, "carousel", StringComparison.OrdinalIgnoreCase)
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
    /// Extracts the clientId GUID and relative path from
    /// <c>{orgId}/{clientId}/{analysisId}/{relativePath}</c>.
    /// Returns false if the path doesn't conform to the expected layout.
    /// </summary>
    private static bool TryExtractClientAndRelativePath(
        string storagePath,
        Guid organizationId,
        Guid analysisId,
        out Guid clientId,
        out string relativePath)
    {
        clientId = Guid.Empty;
        relativePath = string.Empty;

        var segments = storagePath.Split('/');
        if (segments.Length < 4)
            return false;

        if (!Guid.TryParse(segments[0], out var pathOrgId) || pathOrgId != organizationId)
            return false;

        if (!Guid.TryParse(segments[1], out var pathClientId))
            return false;

        if (!Guid.TryParse(segments[2], out var pathAnalysisId) || pathAnalysisId != analysisId)
            return false;

        relativePath = string.Join("/", segments.Skip(3)).Trim('/');
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        clientId = pathClientId;
        return true;
    }

    /// <summary>
    /// Derives a format key from a relative asset path.
    /// Examples:
    /// <list type="bullet">
    ///   <item><c>carousel/slide_1.png</c> → <c>carousel</c></item>
    ///   <item><c>flyer.png</c> → <c>flyer</c></item>
    ///   <item><c>reel-script.txt</c> → <c>reel-script</c></item>
    /// </list>
    /// </summary>
    private static string DeriveFormatKey(string relativePath)
    {
        var firstSegment = relativePath.Split('/')[0];
        if (string.Equals(firstSegment, "carousel", StringComparison.OrdinalIgnoreCase))
        {
            return "carousel";
        }

        var dotIndex = firstSegment.LastIndexOf('.');
        return dotIndex > 0 ? firstSegment[..dotIndex] : firstSegment;
    }

    private static IReadOnlyList<ParsedFormatAsset> SelectLatestRowsPerClientFormat(
        IReadOnlyList<ParsedFormatAsset> parsedRows)
    {
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
        string RelativePath,
        string FormatKey,
        FormatAssetRow Row);
}
