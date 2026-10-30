using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Tenants;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Tenancy;

internal sealed class EfTenantMembershipService : ITenantMembershipService
{
    private readonly AppIdentityDbContext _dbContext;

    public EfTenantMembershipService(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> IsMemberAsync(TenantId tenantId, Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.TenantMemberships.AnyAsync(
            x => x.TenantId == tenantId.Value && x.UserId == userId,
            cancellationToken);

    public async Task<TenantRole?> GetRoleAsync(TenantId tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        var role = await _dbContext.TenantMemberships
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId.Value && x.UserId == userId)
            .Select(x => x.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return role is null ? null : Enum.Parse<TenantRole>(role);
    }

    public Task AddAsync(TenantMembership membership, CancellationToken cancellationToken = default)
    {
        _dbContext.TenantMemberships.Add(new TenantMembershipData
        {
            TenantId = membership.TenantId.Value,
            UserId = membership.UserId,
            Role = membership.Role.ToString(),
            CreatedAtUtc = membership.CreatedAtUtc,
        });

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<TenantMembership>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var memberships = await _dbContext.TenantMemberships
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        return memberships
            .Select(x => new TenantMembership(
                new TenantId(x.TenantId),
                x.UserId,
                Enum.Parse<TenantRole>(x.Role),
                x.CreatedAtUtc))
            .ToList();
    }
}
