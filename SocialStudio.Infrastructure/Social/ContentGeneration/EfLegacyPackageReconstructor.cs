using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Social.ContentFormats;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Infrastructure.Social.ContentGeneration;

/// <summary>
/// EF + storage implementation of <see cref="ILegacyPackageReconstructor"/>.
///
/// For every format result still keyed with a legacy taxonomy key it:
/// 1. Computes the canonical key and the canonical storage paths for its assets.
/// 2. If a canonical row already occupies the target path (a newer regeneration),
///    treats the legacy result as a stale duplicate and deletes it (objects + rows).
/// 3. Otherwise renames each stored object (open → put → delete) and rewrites the
///    asset rows' storage_path / filename, then re-keys the result.
///
/// Idempotent: a second run finds no legacy keys and returns a no-op report.
/// </summary>
public sealed class EfLegacyPackageReconstructor : ILegacyPackageReconstructor
{
    private readonly AppIdentityDbContext _db;
    private readonly IGeneratedAssetStorage _storage;
    private readonly ILogger<EfLegacyPackageReconstructor> _logger;

    public EfLegacyPackageReconstructor(
        AppIdentityDbContext db,
        IGeneratedAssetStorage storage,
        ILogger<EfLegacyPackageReconstructor> logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(logger);

        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public async Task<LegacyPackageReconstructionReport> ReconstructAsync(CancellationToken ct)
    {
        var legacyKeys = LegacyFormatReKey.LegacyKeys.ToList();

        var legacyResults = await _db.ContentGenerationFormatResults
            .Include(r => r.Assets)
            .Where(r => legacyKeys.Contains(r.FormatKey))
            .ToListAsync(ct);

        if (legacyResults.Count == 0)
        {
            return new LegacyPackageReconstructionReport(0, 0, 0);
        }

        // Snapshot of every storage path currently in use, to detect collisions with
        // a newer canonical regeneration that already owns the target path.
        var existingPaths = await _db.ContentGenerationFormatAssets
            .AsNoTracking()
            .Select(a => a.StoragePath)
            .ToListAsync(ct);
        var existingPathSet = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);

        var resultsReKeyed = 0;
        var assetsRenamed = 0;
        var staleResultsDeleted = 0;

        foreach (var result in legacyResults)
        {
            if (!LegacyFormatReKey.TryReKeyFormat(result.FormatKey, out var canonicalKey))
            {
                continue; // defensive: should not happen given the filter
            }

            var moves = result.Assets
                .Select(a => new AssetMove(a, ComputeCanonicalPath(a.StoragePath, canonicalKey)))
                .ToList();

            // Collision: a different result already owns one of the target paths → the
            // legacy result is a superseded duplicate. Drop it (objects + rows).
            var collides = moves.Any(m =>
                !string.Equals(m.NewStoragePath, m.Asset.StoragePath, StringComparison.OrdinalIgnoreCase)
                && existingPathSet.Contains(m.NewStoragePath));

            if (collides)
            {
                await DeleteStaleResultAsync(result, ct);
                staleResultsDeleted++;
                continue;
            }

            foreach (var move in moves)
            {
                if (string.Equals(move.NewStoragePath, move.Asset.StoragePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // already canonical
                }

                await RenameStoredObjectAsync(move.Asset.StoragePath, move.NewStoragePath, move.Asset.MimeType, ct);

                existingPathSet.Remove(move.Asset.StoragePath);
                existingPathSet.Add(move.NewStoragePath);

                move.Asset.StoragePath = move.NewStoragePath;
                move.Asset.Filename = LastSegment(move.NewStoragePath);
                move.Asset.UpdatedAt = DateTime.UtcNow;
                assetsRenamed++;
            }

            result.FormatKey = canonicalKey;
            result.UpdatedAt = DateTime.UtcNow;
            resultsReKeyed++;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Legacy package reconstruction complete: {ReKeyed} results re-keyed, {Renamed} assets renamed, {Stale} stale results deleted.",
            resultsReKeyed, assetsRenamed, staleResultsDeleted);

        return new LegacyPackageReconstructionReport(resultsReKeyed, assetsRenamed, staleResultsDeleted);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Transforms a full storage path ({org}/{client}/{analysis}/{relative}) to its
    /// canonical equivalent, re-keying only the relative portion.
    /// </summary>
    private static string ComputeCanonicalPath(string fullStoragePath, string canonicalKey)
    {
        var segments = fullStoragePath.Split('/');
        if (segments.Length < 4)
        {
            return fullStoragePath; // unexpected layout — leave untouched
        }

        var prefix = string.Join('/', segments.Take(3));
        var relative = string.Join('/', segments.Skip(3));
        var newRelative = LegacyFormatReKey.ReKeyRelativePath(relative, canonicalKey);
        return $"{prefix}/{newRelative}";
    }

    private async Task RenameStoredObjectAsync(string oldPath, string newPath, string mime, CancellationToken ct)
    {
        var open = await _storage.OpenAsync(oldPath, ct);
        if (open.IsFailure)
        {
            // Object already moved on a previous partial run, or never existed.
            // The DB row is still re-pointed to the canonical path so listing/assemble stay consistent.
            _logger.LogWarning(
                "Reconstruction: source object '{OldPath}' not readable ({Error}); re-pointing DB row to '{NewPath}' without a storage copy.",
                oldPath, open.Error.Code, newPath);
            return;
        }

        byte[] bytes;
        string effectiveMime;
        using (var stream = open.Value)
        {
            effectiveMime = string.IsNullOrWhiteSpace(stream.Mime) ? mime : stream.Mime;
            using var buffer = new MemoryStream();
            await stream.Content.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        var key = BuildKeyFromFullPath(newPath);
        await _storage.PutAssetAsync(key, new StoredAssetContent(bytes, effectiveMime), ct);
        await _storage.DeleteAsync(oldPath, ct);
    }

    private async Task DeleteStaleResultAsync(ContentGenerationFormatResultData result, CancellationToken ct)
    {
        foreach (var asset in result.Assets)
        {
            await _storage.DeleteAsync(asset.StoragePath, ct);
        }

        _db.ContentGenerationFormatAssets.RemoveRange(result.Assets);
        _db.ContentGenerationFormatResults.Remove(result);

        _logger.LogInformation(
            "Reconstruction: dropped stale legacy result {ResultId} ({FormatKey}); a canonical version already owns its target paths.",
            result.Id, result.FormatKey);
    }

    private static StoredAssetKey BuildKeyFromFullPath(string fullStoragePath)
    {
        var segments = fullStoragePath.Split('/');
        var orgId = Guid.Parse(segments[0]);
        var clientId = Guid.Parse(segments[1]);
        var analysisId = Guid.Parse(segments[2]);
        var relative = string.Join('/', segments.Skip(3));
        return new StoredAssetKey(orgId, clientId, analysisId, relative);
    }

    private static string LastSegment(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx >= 0 ? path[(idx + 1)..] : path;
    }

    private sealed record AssetMove(ContentGenerationFormatAssetData Asset, string NewStoragePath);
}
