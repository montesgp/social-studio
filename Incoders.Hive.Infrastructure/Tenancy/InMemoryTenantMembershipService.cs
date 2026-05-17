using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Domain.Tenants;

namespace Incoders.Hive.Infrastructure.Tenancy;

internal sealed class InMemoryTenantMembershipService : ITenantMembershipService
{
    private readonly InMemoryTenantStore _store;

    public InMemoryTenantMembershipService(InMemoryTenantStore store)
    {
        _store = store;
    }

    public Task<bool> IsMemberAsync(TenantId tenantId, Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Memberships.Any(m => m.TenantId == tenantId && m.UserId == userId));

    public Task<TenantRole?> GetRoleAsync(TenantId tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        var membership = _store.Memberships.FirstOrDefault(m => m.TenantId == tenantId && m.UserId == userId);
        return Task.FromResult<TenantRole?>(membership?.Role);
    }

    public Task AddAsync(TenantMembership membership, CancellationToken cancellationToken = default)
    {
        _store.Memberships.Add(membership);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TenantMembership>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantMembership> list = _store.Memberships
            .Where(m => m.UserId == userId)
            .ToList();
        return Task.FromResult(list);
    }
}
