using Incoders.Hive.Domain.Tenants;

namespace Incoders.Hive.Application.Abstractions.Tenancy;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Tenant> Items, int TotalCount)> ListAsync(
        TenantQuery query,
        CancellationToken cancellationToken = default);

    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);

    void Update(Tenant tenant);
}

public sealed record TenantQuery(
    string? NameContains,
    bool IncludeInactive,
    int Page,
    int PageSize);
