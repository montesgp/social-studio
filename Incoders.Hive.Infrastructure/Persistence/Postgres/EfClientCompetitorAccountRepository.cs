using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.ClientCompetitorAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfClientCompetitorAccountRepository : IClientCompetitorAccountRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfClientCompetitorAccountRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ClientCompetitorAccount?> GetByIdAsync(
        ClientCompetitorAccountId id,
        Guid organizationId,
        CancellationToken ct)
    {
        var row = await _dbContext.ClientCompetitorAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<(IReadOnlyList<ClientCompetitorAccount> Items, int TotalCount)> ListByClientAsync(
        ClientId clientId,
        Guid organizationId,
        bool includeInactive,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        IQueryable<ClientCompetitorAccountData> source = _dbContext.ClientCompetitorAccounts
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
        ClientCompetitorAccountId? excludeId,
        CancellationToken ct)
    {
        var query = _dbContext.ClientCompetitorAccounts
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

    public async Task<IReadOnlyList<ClientCompetitorAccount>> GetAllByClientAsync(
        ClientId clientId,
        Guid organizationId,
        CancellationToken ct)
    {
        var rows = await _dbContext.ClientCompetitorAccounts
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
        var rows = await _dbContext.ClientCompetitorAccounts
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

    public async Task AddAsync(ClientCompetitorAccount competitorAccount, CancellationToken ct)
    {
        await _dbContext.ClientCompetitorAccounts.AddAsync(ToData(competitorAccount), ct);
    }

    public void Update(ClientCompetitorAccount account)
    {
        var entity = _dbContext.ClientCompetitorAccounts.Local.FirstOrDefault(x => x.Id == account.Id.Value)
                     ?? _dbContext.ClientCompetitorAccounts.First(x => x.Id == account.Id.Value);

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
            throw new ClientCompetitorAccountProfileUrlConflictException(ex);
        }
    }

    private static ClientCompetitorAccount ToDomain(ClientCompetitorAccountData d) =>
        ClientCompetitorAccount.Restore(
            new ClientCompetitorAccountId(d.Id),
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

    private static ClientCompetitorAccountData ToData(ClientCompetitorAccount a) => new()
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
        pg.ConstraintName is "ix_client_competitor_accounts_client_network_url";
}

/// <summary>
/// Thrown when the composite unique constraint on (client_id, social_network_id, profile_url) is violated.
/// </summary>
internal sealed class ClientCompetitorAccountProfileUrlConflictException : Exception
{
    public ClientCompetitorAccountProfileUrlConflictException(Exception inner)
        : base("ProfileUrl conflict on (ClientId, SocialNetworkId, ProfileUrl).", inner)
    {
    }
}
