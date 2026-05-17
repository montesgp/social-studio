using Incoders.Hive.Domain.Tenants;

namespace Incoders.Hive.Application.Abstractions.Tenancy;

/// <summary>
/// Authorizes user-tenant relationships. Used by middleware and handlers to enforce isolation.
/// </summary>
public interface ITenantMembershipService
{
    Task<bool> IsMemberAsync(TenantId tenantId, Guid userId, CancellationToken cancellationToken = default);

    Task<TenantRole?> GetRoleAsync(TenantId tenantId, Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(TenantMembership membership, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantMembership>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
