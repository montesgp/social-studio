namespace Incoders.Hive.Application.Abstractions.Social;

/// <summary>
/// Hard-delete port for the temporary MVP sysadmin cleanup operation.
/// This is deliberately separate from <see cref="ISocialClientRepository"/> so normal client
/// management continues to prefer archive/reactivate semantics.
/// </summary>
public interface ISocialClientHardDeleteRepository
{
    /// <summary>
    /// Marks the client and all currently known client-scoped database rows for hard deletion.
    /// The returned storage refs are cleaned up by the application handler after the database
    /// transaction is committed.
    /// </summary>
    Task<SocialClientHardDeleteResult?> HardDeleteAsync(
        Guid clientId,
        CancellationToken cancellationToken = default);
}

public sealed record SocialClientHardDeleteResult(
    Guid ClientId,
    Guid OrganizationId,
    string ClientName,
    IReadOnlyList<string> GeneratedPackageStorageUris,
    IReadOnlyList<string> GeneratedAssetStoragePaths);
