using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Application port for durable per-asset storage.
/// Implemented by <c>SupabaseStorageAdapter</c> in Infrastructure.
/// No Supabase, HTTP, or provider-specific types cross this boundary.
/// </summary>
public interface IGeneratedAssetStorage
{
    /// <summary>Uploads (upserts) a binary asset to storage at the path derived from <paramref name="key"/>.</summary>
    Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct);

    /// <summary>Creates a folder placeholder (trailing-slash empty object) at <paramref name="folderPath"/>.</summary>
    Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct);

    /// <summary>Lists all objects whose storage paths start with <paramref name="prefix"/>.</summary>
    Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct);

    /// <summary>Opens an existing object for reading. Returns <c>NotFound</c> when the object does not exist.</summary>
    Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct);

    /// <summary>Permanently removes an object from storage.</summary>
    Task DeleteAsync(string storagePath, CancellationToken ct);

    /// <summary>
    /// Attempts to build a publicly-fetchable URL (no auth headers required) for the object at
    /// <paramref name="storagePath"/> — used by outbound integrations (e.g. the Instagram Graph API)
    /// that fetch media themselves rather than receiving bytes from us. Default implementation fails
    /// with <see cref="StorageErrors.PublicUrlUnsupported"/>; only backends with a genuine public-URL
    /// scheme (e.g. a public Supabase Storage bucket) override it. Never throws.
    /// </summary>
    Result<string> TryGetPublicUrl(string storagePath) =>
        Result<string>.Failure(StorageErrors.PublicUrlUnsupported);
}

/// <summary>Identifies the storage path for an asset: {orgId}/{clientId}/{analysisId}/{relativePath}.</summary>
public sealed record StoredAssetKey(
    Guid OrganizationId,
    Guid ClientId,
    Guid AnalysisId,
    /// <summary>Relative path within the analysis folder, e.g. "story.png" or "carousel/slide_1.png".</summary>
    string RelativePath);

/// <summary>Binary content and MIME type for a storage PUT.</summary>
public sealed record StoredAssetContent(byte[] Bytes, string Mime);

/// <summary>Reference returned after a successful PUT.</summary>
public sealed record StoredAssetRef(
    string StoragePath,
    string Mime,
    long SizeBytes,
    string ContentHash);

/// <summary>Metadata entry returned from a storage LIST operation.</summary>
public sealed record StoredAssetEntry(
    string StoragePath,
    string Name,
    long SizeBytes,
    string? Mime,
    DateTimeOffset? UpdatedAt,
    bool IsFolder);

/// <summary>Stream wrapper returned by <see cref="IGeneratedAssetStorage.OpenAsync"/>.</summary>
public sealed record StoredAssetStream(Stream Content, string Mime, long SizeBytes) : IDisposable
{
    public void Dispose() => Content.Dispose();
}

/// <summary>
/// Thrown when a Supabase Storage PUT, DELETE, or LIST fails with a non-2xx HTTP response.
/// Caught in the pipeline to record the format as failed without orphan asset rows.
/// </summary>
public sealed class GeneratedAssetStorageException(string message) : Exception(message);

/// <summary>Typed errors for <see cref="IGeneratedAssetStorage"/>.</summary>
public static class StorageErrors
{
    public static readonly Error NotFound =
        Error.NotFound("storage.not-found", "storage.notFound");

    public static readonly Error UploadFailed =
        Error.Failure("storage.upload-failed", "storage.uploadFailed");

    public static readonly Error ListFailed =
        Error.Failure("storage.list-failed", "storage.listFailed");

    public static readonly Error PublicUrlUnsupported =
        Error.Failure("storage.public-url-unsupported", "storage.publicUrlUnsupported");
}
