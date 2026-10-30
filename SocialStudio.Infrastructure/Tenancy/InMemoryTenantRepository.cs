using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Tenants;

namespace SocialStudio.Infrastructure.Tenancy;

internal sealed class InMemoryTenantRepository : ITenantRepository
{
    private readonly InMemoryTenantStore _store;

    public InMemoryTenantRepository(InMemoryTenantStore store)
    {
        _store = store;
    }

    public Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default)
    {
        _store.Tenants.TryGetValue(id, out var tenant);
        return Task.FromResult(tenant);
    }

    public Task<(IReadOnlyList<Tenant> Items, int TotalCount)> ListAsync(
        TenantQuery query,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Tenant> source = _store.Tenants.Values;

        if (!query.IncludeInactive)
        {
            source = source.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.NameContains))
        {
            var needle = query.NameContains.Trim();
            source = source.Where(x => x.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = source
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.CreatedAtUtc)
            .ToList();

        var totalCount = filtered.Count;
        IReadOnlyList<Tenant> page = filtered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult((page, totalCount));
    }

    public Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        if (!_store.Tenants.TryAdd(tenant.Id, tenant))
        {
            throw new InvalidOperationException($"Tenant '{tenant.Id}' already exists.");
        }
        return Task.CompletedTask;
    }

    public void Update(Tenant tenant)
    {
        _store.Tenants[tenant.Id] = tenant;
    }
}
