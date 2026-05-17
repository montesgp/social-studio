using System.Collections.Concurrent;
using Incoders.Hive.Domain.Tenants;

namespace Incoders.Hive.Infrastructure.Tenancy;

public sealed class InMemoryTenantStore
{
    internal ConcurrentDictionary<TenantId, Tenant> Tenants { get; } = new();

    internal ConcurrentBag<TenantMembership> Memberships { get; } = new();
}
