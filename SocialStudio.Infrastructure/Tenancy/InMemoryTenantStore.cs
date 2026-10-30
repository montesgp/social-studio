using System.Collections.Concurrent;
using SocialStudio.Domain.Tenants;

namespace SocialStudio.Infrastructure.Tenancy;

public sealed class InMemoryTenantStore
{
    internal ConcurrentDictionary<TenantId, Tenant> Tenants { get; } = new();

    internal ConcurrentBag<TenantMembership> Memberships { get; } = new();
}
