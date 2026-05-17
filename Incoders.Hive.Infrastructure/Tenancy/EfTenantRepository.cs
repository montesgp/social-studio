using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Domain.Tenants;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Tenancy;

internal sealed class EfTenantRepository : ITenantRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfTenantRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public async Task<(IReadOnlyList<Tenant> Items, int TotalCount)> ListAsync(
        TenantQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<TenantData> source = _dbContext.Tenants.AsNoTracking();

        if (!query.IncludeInactive)
        {
            source = source.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.NameContains))
        {
            var needle = query.NameContains.Trim();
            source = source.Where(x => x.Name.Contains(needle));
        }

        var totalCount = await source.CountAsync(cancellationToken);
        var rows = await source
            .OrderBy(x => x.Name)
            .ThenBy(x => x.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return (rows.Select(ToDomain).ToList(), totalCount);
    }

    public Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        _dbContext.Tenants.Add(ToData(tenant));
        return Task.CompletedTask;
    }

    public void Update(Tenant tenant)
    {
        var entity = _dbContext.Tenants.Local.FirstOrDefault(x => x.Id == tenant.Id.Value)
                     ?? _dbContext.Tenants.First(x => x.Id == tenant.Id.Value);

        entity.Name = tenant.Name;
        entity.IsActive = tenant.IsActive;
        entity.DeactivatedAtUtc = tenant.DeactivatedAtUtc;
        entity.UpdatedAtUtc = tenant.UpdatedAtUtc;
    }

    private static Tenant ToDomain(TenantData row) =>
        Tenant.Restore(
            new TenantId(row.Id),
            row.Name,
            row.OwnerUserId,
            row.IsActive,
            row.DeactivatedAtUtc,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static TenantData ToData(Tenant tenant) =>
        new()
        {
            Id = tenant.Id.Value,
            Name = tenant.Name,
            OwnerUserId = tenant.OwnerUserId,
            IsActive = tenant.IsActive,
            DeactivatedAtUtc = tenant.DeactivatedAtUtc,
            CreatedAtUtc = tenant.CreatedAtUtc,
            UpdatedAtUtc = tenant.UpdatedAtUtc,
        };
}
