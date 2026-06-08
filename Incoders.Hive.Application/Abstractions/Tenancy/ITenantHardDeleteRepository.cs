namespace Incoders.Hive.Application.Abstractions.Tenancy;

/// <summary>
/// Hard-delete port for the temporary MVP sysadmin organization cleanup operation.
/// This stays separate from <see cref="ITenantRepository"/> so the normal organization lifecycle
/// keeps archive/reactivate semantics.
/// </summary>
public interface ITenantHardDeleteRepository
{
    /// <summary>
    /// Marks the organization and all currently known organization-scoped database rows for hard deletion.
    /// Returned storage refs are cleaned up by the application handler after the database transaction commits.
    /// </summary>
    Task<TenantHardDeleteResult?> HardDeleteAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}

public sealed record TenantHardDeleteResult(
    Guid OrganizationId,
    string OrganizationName,
    IReadOnlyList<Guid> ClientIds,
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<string> GeneratedPackageStorageUris,
    IReadOnlyList<string> GeneratedAssetStoragePaths);
