using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.ClientReferentAccounts;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Domain.Social.Networks;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfClientReferentAccountRepository : IClientReferentAccountRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfClientReferentAccountRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ClientReferentAccount?> GetByIdAsync(
        ClientReferentAccountId id,
        Guid organizationId,
        CancellationToken ct)
    {
        var row = await _dbContext.ClientReferentAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<(IReadOnlyList<ClientReferentAccount> Items, int TotalCount)> ListByClientAsync(
        ClientId clientId,
        Guid organizationId,
        bool includeInactive,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        IQueryable<ClientReferentAccountData> source = _dbContext.ClientReferentAccounts
            .AsNoTracking()
            .Where(x => x.ClientId == clientId.Value && x.OrganizationId == organizationId);

        if (!includeInactive)
        {
            source = source.Where(x => x.IsActive);
        }

        var totalCount = await source.CountAsync(ct);
        var rows = await source
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (rows.Select(ToDomain).ToList(), totalCount);
    }

    public async Task<bool> ExistsByProfileUrlAsync(
        ClientId clientId,
        SocialNetworkId socialNetworkId,
        string profileUrl,
        ClientReferentAccountId? excludeId,
        CancellationToken ct)
    {
        var query = _dbContext.ClientReferentAccounts
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

    public async Task AddAsync(ClientReferentAccount referentAccount, CancellationToken ct)
    {
        await _dbContext.ClientReferentAccounts.AddAsync(ToData(referentAccount), ct);
    }

    public void Update(ClientReferentAccount account)
    {
        var entity = _dbContext.ClientReferentAccounts.Local.FirstOrDefault(x => x.Id == account.Id.Value)
                     ?? _dbContext.ClientReferentAccounts.First(x => x.Id == account.Id.Value);

        // Only mutable fields — ClientId, OrganizationId, SocialNetworkId are immutable (R-NFR-002)
        entity.ProfileUrl = account.ProfileUrl;
        entity.Handle = account.Handle;
        entity.Label = account.Label;
        entity.Notes = account.Notes;
        entity.IsActive = account.IsActive;
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
            // Defense-in-depth: handler pre-checks via ExistsByProfileUrlAsync, but race conditions
            // could still hit the DB unique constraint. Translate to conflict exception.
            throw new ClientReferentAccountProfileUrlConflictException(ex);
        }
    }

    private static ClientReferentAccount ToDomain(ClientReferentAccountData d) =>
        ClientReferentAccount.Restore(
            new ClientReferentAccountId(d.Id),
            new ClientId(d.ClientId),
            d.OrganizationId,
            new SocialNetworkId(d.SocialNetworkId),
            d.ProfileUrl,
            d.Handle,
            d.Label,
            d.Notes,
            d.IsActive,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);

    private static ClientReferentAccountData ToData(ClientReferentAccount a) => new()
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
        CreatedAtUtc = a.CreatedAtUtc,
        UpdatedAtUtc = a.UpdatedAtUtc,
    };

    private static bool IsPgUniqueViolationOnCompositeIndex(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg &&
        pg.SqlState == "23505" &&
        pg.ConstraintName is "ix_client_referent_accounts_client_network_url";
}

/// <summary>
/// Thrown when the composite unique constraint on (client_id, social_network_id, profile_url) is violated.
/// Caught by Create/Update handlers as defense-in-depth against race conditions.
/// </summary>
internal sealed class ClientReferentAccountProfileUrlConflictException : Exception
{
    public ClientReferentAccountProfileUrlConflictException(Exception inner)
        : base("ProfileUrl conflict on (ClientId, SocialNetworkId, ProfileUrl).", inner)
    {
    }
}
