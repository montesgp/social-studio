using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Social.ClientOwnAccounts;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class EfClientOwnAccountRepository : IClientOwnAccountRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfClientOwnAccountRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ClientOwnAccount?> GetByIdAsync(
        ClientOwnAccountId id,
        Guid organizationId,
        CancellationToken ct)
    {
        var row = await _dbContext.ClientOwnAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<(IReadOnlyList<ClientOwnAccount> Items, int TotalCount)> ListByClientAsync(
        ClientId clientId,
        Guid organizationId,
        bool includeInactive,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        IQueryable<ClientOwnAccountData> source = _dbContext.ClientOwnAccounts
            .AsNoTracking()
            .Where(x => x.ClientId == clientId.Value && x.OrganizationId == organizationId);

        if (!includeInactive)
        {
            source = source.Where(x => x.IsActive);
        }

        var totalCount = await source.CountAsync(ct);
        var rows = await source
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (rows.Select(ToDomain).ToList(), totalCount);
    }

    public async Task<bool> ExistsByProfileUrlAsync(
        ClientId clientId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        ClientOwnAccountId? excludeId,
        CancellationToken ct)
    {
        var query = _dbContext.ClientOwnAccounts
            .AsNoTracking()
            .Where(x =>
                x.ClientId == clientId.Value &&
                x.SocialNetworkId == socialNetworkId.Value &&
                x.ProfileUrl == profileUrl);

        if (excludeId.HasValue)
        {
            query = query.Where(x => x.Id != excludeId.Value.Value);
        }

        return await query.AnyAsync(ct);
    }

    public async Task<IReadOnlyList<ClientOwnAccount>> GetAllByClientAsync(
        ClientId clientId,
        Guid organizationId,
        CancellationToken ct)
    {
        var rows = await _dbContext.ClientOwnAccounts
            .AsNoTracking()
            .Where(x => x.ClientId == clientId.Value && x.OrganizationId == organizationId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task ReorderAsync(
        ClientId clientId,
        Guid organizationId,
        IReadOnlyList<Guid> orderedIds,
        CancellationToken ct)
    {
        var rows = await _dbContext.ClientOwnAccounts
            .Where(x => x.ClientId == clientId.Value && x.OrganizationId == organizationId)
            .ToListAsync(ct);

        var rowById = rows.ToDictionary(r => r.Id);
        var nowUtc = DateTime.UtcNow;

        for (var i = 0; i < orderedIds.Count; i++)
        {
            if (rowById.TryGetValue(orderedIds[i], out var row))
            {
                row.SortOrder = i;
                row.UpdatedAtUtc = nowUtc;
            }
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AddAsync(ClientOwnAccount ownAccount, CancellationToken ct)
    {
        await _dbContext.ClientOwnAccounts.AddAsync(ToData(ownAccount), ct);
    }

    public void Update(ClientOwnAccount account)
    {
        var entity = _dbContext.ClientOwnAccounts.Local.FirstOrDefault(x => x.Id == account.Id.Value)
                     ?? _dbContext.ClientOwnAccounts.First(x => x.Id == account.Id.Value);

        entity.SocialNetworkId = account.SocialNetworkId.Value;
        entity.ProfileUrl = account.ProfileUrl;
        entity.Handle = account.Handle;
        entity.Label = account.Label;
        entity.Notes = account.Notes;
        entity.IsActive = account.IsActive;
        entity.SortOrder = account.SortOrder;
        entity.UpdatedAtUtc = account.UpdatedAtUtc;
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsPgUniqueViolationOnCompositeIndex(ex))
        {
            throw new ClientOwnAccountProfileUrlConflictException(ex);
        }
    }

    private static ClientOwnAccount ToDomain(ClientOwnAccountData d) =>
        ClientOwnAccount.Restore(
            new ClientOwnAccountId(d.Id),
            new ClientId(d.ClientId),
            d.OrganizationId,
            new SocialNetworkId(d.SocialNetworkId),
            d.ProfileUrl,
            d.Handle,
            d.Label,
            d.Notes,
            d.IsActive,
            d.CreatedAtUtc,
            d.UpdatedAtUtc,
            d.SortOrder);

    private static ClientOwnAccountData ToData(ClientOwnAccount a) => new()
    {
        Id = a.Id.Value,
        ClientId = a.ClientId.Value,
        OrganizationId = a.OrganizationId,
        SocialNetworkId = a.SocialNetworkId.Value,
        ProfileUrl = a.ProfileUrl,
        Handle = a.Handle,
        Label = a.Label,
        Notes = a.Notes,
        IsActive = a.IsActive,
        SortOrder = a.SortOrder,
        CreatedAtUtc = a.CreatedAtUtc,
        UpdatedAtUtc = a.UpdatedAtUtc,
    };

    private static bool IsPgUniqueViolationOnCompositeIndex(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg &&
        pg.SqlState == "23505" &&
        pg.ConstraintName is "ix_client_own_accounts_client_network_url";
}

/// <summary>
/// Thrown when the composite unique constraint on (client_id, social_network_id, profile_url) is violated.
/// </summary>
internal sealed class ClientOwnAccountProfileUrlConflictException : Exception
{
    public ClientOwnAccountProfileUrlConflictException(Exception inner)
        : base("ProfileUrl conflict on (ClientId, SocialNetworkId, ProfileUrl).", inner)
    {
    }
}
