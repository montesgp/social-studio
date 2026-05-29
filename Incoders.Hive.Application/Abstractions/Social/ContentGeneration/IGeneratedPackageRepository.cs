namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Port for persisting and querying generated packages (1:1 with jobs).
/// </summary>
public interface IGeneratedPackageRepository
{
    Task InsertAsync(GeneratedPackage package, CancellationToken ct);

    Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct);
}

/// <summary>
/// Application-layer projection of a generated package row.
/// </summary>
public sealed record GeneratedPackage(
    Guid Id,
    Guid JobId,
    string StorageUri,
    long SizeBytes,
    string ManifestJson,
    DateTime? ExpiresAt,
    DateTime CreatedAt);
