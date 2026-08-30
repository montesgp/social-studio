using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// Dev-local filesystem implementation of <see cref="IGeneratedAssetStorage"/>.
/// Routes all generated-asset traffic (slides, backgrounds, client reference images) to a
/// local root directory so local iteration consumes ZERO Supabase Storage egress/quota.
/// <para>
/// Storage-path format is byte-for-byte identical to <c>SupabaseStorageAdapter.BuildStoragePath</c>:
/// <c>"{organizationId}/{clientId}/{analysisId}/{relativePath}"</c> (forward slashes), so switching
/// <c>Storage:AssetProvider</c> between "Local" and "Supabase" never changes the shape of a
/// <see cref="StoredAssetRef.StoragePath"/>.
/// </para>
/// <para>
/// MIME type is not recoverable from raw file bytes, so it is persisted in a small sidecar file
/// (<c>{file}.mime</c>) written alongside every asset and read back on <see cref="OpenAsync"/>/<see cref="ListAsync"/>.
/// </para>
/// </summary>
public sealed class LocalFileGeneratedAssetStorage : IGeneratedAssetStorage
{
    private const string MimeSidecarExtension = ".mime";

    private readonly string _rootPath;

    /// <summary>Creates storage backed by the given root directory. The directory is created lazily on first write.</summary>
    public LocalFileGeneratedAssetStorage(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.GetFullPath(rootPath);
    }

    // -----------------------------------------------------------------------
    // IGeneratedAssetStorage
    // -----------------------------------------------------------------------

    public async Task<StoredAssetRef> PutAssetAsync(
        StoredAssetKey key,
        StoredAssetContent content,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(content);
        ValidateRelativePath(key.RelativePath);

        var storagePath = BuildStoragePath(key);
        var filePath = ResolvePath(storagePath);

        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(filePath, content.Bytes, ct);
        await File.WriteAllTextAsync(MimeSidecarPath(filePath), content.Mime, ct);

        var hash = AssetContentHasher.ComputeSha256(content.Bytes);
        return new StoredAssetRef(storagePath, content.Mime, content.Bytes.Length, hash);
    }

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var normalized = folderPath.TrimEnd('/');
        ValidateRelativePath(normalized);

        var directoryPath = ResolvePath(normalized);
        Directory.CreateDirectory(directoryPath);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var entries = new List<StoredAssetEntry>();
        if (!Directory.Exists(_rootPath))
        {
            return Task.FromResult<IReadOnlyList<StoredAssetEntry>>(entries);
        }

        foreach (var filePath in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
        {
            if (IsMimeSidecar(filePath))
            {
                continue;
            }

            var storagePath = ToStoragePath(filePath);
            if (!storagePath.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var info = new FileInfo(filePath);
            var mime = ReadMimeSidecar(filePath) ?? InferMimeFromExtension(filePath);
            var name = Path.GetFileName(filePath);

            entries.Add(new StoredAssetEntry(storagePath, name, info.Length, mime, info.LastWriteTimeUtc, IsFolder: false));
        }

        return Task.FromResult<IReadOnlyList<StoredAssetEntry>>(entries);
    }

    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(storagePath);

        string filePath;
        try
        {
            filePath = ResolvePath(storagePath);
        }
        catch (ArgumentException)
        {
            // Path escapes the storage root — treat exactly like "does not exist".
            return Task.FromResult(Result<StoredAssetStream>.Failure(StorageErrors.NotFound));
        }

        if (!File.Exists(filePath))
        {
            return Task.FromResult(Result<StoredAssetStream>.Failure(StorageErrors.NotFound));
        }

        var mime = ReadMimeSidecar(filePath) ?? InferMimeFromExtension(filePath);
        var size = new FileInfo(filePath).Length;

        Stream stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);

        return Task.FromResult(Result<StoredAssetStream>.Success(new StoredAssetStream(stream, mime, size)));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(storagePath);

        string filePath;
        try
        {
            filePath = ResolvePath(storagePath);
        }
        catch (ArgumentException)
        {
            // Path escapes the storage root — nothing to delete, do not throw.
            return Task.CompletedTask;
        }

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        var sidecar = MimeSidecarPath(filePath);
        if (File.Exists(sidecar))
        {
            File.Delete(sidecar);
        }

        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Path mapping — mirrors SupabaseStorageAdapter.BuildStoragePath exactly.
    // -----------------------------------------------------------------------

    private static string BuildStoragePath(StoredAssetKey key) =>
        $"{key.OrganizationId}/{key.ClientId}/{key.AnalysisId}/{key.RelativePath}";

    /// <summary>
    /// Maps a "{org}/{client}/{analysis}/{relativePath}"-shaped storage path onto a file under
    /// the configured root, guarding against path traversal escaping that root.
    /// </summary>
    private string ResolvePath(string storagePath)
    {
        var segments = storagePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var combined = segments.Length == 0
            ? _rootPath
            : Path.Combine(_rootPath, Path.Combine(segments));

        var fullPath = Path.GetFullPath(combined);

        if (!IsUnderRoot(fullPath))
        {
            throw new ArgumentException(
                $"Storage path escapes the storage root: '{storagePath}'",
                nameof(storagePath));
        }

        return fullPath;
    }

    private bool IsUnderRoot(string fullPath)
    {
        if (string.Equals(fullPath, _rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootWithSeparator = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private string ToStoragePath(string filePath)
    {
        var relative = Path.GetRelativePath(_rootPath, filePath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("RelativePath must not be empty.", nameof(relativePath));
        }

        var segments = relativePath.Split('/');
        if (segments.Any(s => s is ".." or "."))
        {
            throw new ArgumentException(
                $"RelativePath must not contain '..' or '.' segments: '{relativePath}'",
                nameof(relativePath));
        }
    }

    // -----------------------------------------------------------------------
    // MIME sidecar — bytes alone don't carry the MIME type, so it round-trips via a sidecar file.
    // -----------------------------------------------------------------------

    private static string MimeSidecarPath(string filePath) => filePath + MimeSidecarExtension;

    private static bool IsMimeSidecar(string filePath) =>
        filePath.EndsWith(MimeSidecarExtension, StringComparison.OrdinalIgnoreCase);

    private static string? ReadMimeSidecar(string filePath)
    {
        var sidecar = MimeSidecarPath(filePath);
        return File.Exists(sidecar) ? File.ReadAllText(sidecar).Trim() : null;
    }

    private static string InferMimeFromExtension(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".json" => "application/json",
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".mp4" => "video/mp4",
            _ => "application/octet-stream",
        };
}
