namespace Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

/// <summary>
/// Port for persisting and querying generated packages (1:1 with jobs).
/// </summary>
public interface IGeneratedPackageRepository
{
    Task InsertAsync(GeneratedPackage package, CancellationToken ct);

    Task<GeneratedPackage?> GetByJobIdAsync(Guid jobId, Guid organizationId, CancellationToken ct);

    Task MarkDownloadedAsync(
        Guid jobId,
        Guid organizationId,
        Guid? userId,
        DateTime downloadedAtUtc,
        CancellationToken ct) =>
        Task.CompletedTask;

    Task MarkSentToCanvaAsync(
        Guid jobId,
        Guid organizationId,
        Guid? userId,
        DateTime sentAtUtc,
        CancellationToken ct) =>
        Task.CompletedTask;
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
    DateTime CreatedAt,
    int DownloadCount = 0,
    DateTime? LastDownloadedAtUtc = null,
    Guid? LastDownloadedByUserId = null,
    int CanvaSendCount = 0,
    DateTime? LastSentToCanvaAtUtc = null,
    Guid? LastSentToCanvaByUserId = null);

public sealed record GeneratedPackageActionState(
    int DownloadCount,
    DateTime? LastDownloadedAtUtc,
    int CanvaSendCount,
    DateTime? LastSentToCanvaAtUtc);
