using SocialStudio.Application.Abstractions.Social.Publications;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.ClientAccountConnections;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Networks;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class EfClientAccountConnectionRepository : IClientAccountConnectionRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfClientAccountConnectionRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ClientAccountConnection?> GetByIdAsync(
        Guid organizationId, ClientAccountConnectionId id, CancellationToken ct)
    {
        var row = await _dbContext.ClientAccountConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value && x.OrganizationId == organizationId, ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<ClientAccountConnection>> ListByClientAsync(
        Guid organizationId, Guid clientId, CancellationToken ct)
    {
        var rows = await _dbContext.ClientAccountConnections
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ClientId == clientId)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<ClientAccountConnection?> GetByClientNetworkAsync(
        Guid organizationId, Guid clientId, SocialNetworkId socialNetworkId, CancellationToken ct)
    {
        var row = await _dbContext.ClientAccountConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.OrganizationId == organizationId
                && x.ClientId == clientId
                && x.SocialNetworkId == socialNetworkId.Value, ct);

        return row is null ? null : ToDomain(row);
    }

    public async Task AddAsync(ClientAccountConnection connection, CancellationToken ct)
    {
        _dbContext.ClientAccountConnections.Add(ToData(connection));
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ClientAccountConnection connection, CancellationToken ct)
    {
        var existing = await _dbContext.ClientAccountConnections
            .FirstOrDefaultAsync(x => x.Id == connection.Id.Value && x.OrganizationId == connection.OrganizationId, ct);

        if (existing is null)
        {
            return;
        }

        MapToExisting(connection, existing);
        await _dbContext.SaveChangesAsync(ct);
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    private static ClientAccountConnection ToDomain(ClientAccountConnectionData d) =>
        ClientAccountConnection.Restore(
            new ClientAccountConnectionId(d.Id),
            d.OrganizationId,
            new ClientId(d.ClientId),
            new SocialNetworkId(d.SocialNetworkId),
            new OrganizationIntegrationId(d.OrganizationIntegrationId),
            d.ExternalAccountId,
            d.EncryptedAccessToken,
            d.TokenExpiresAtUtc,
            (ClientAccountConnectionStatus)d.Status,
            d.LastTestedAtUtc,
            d.LastError,
            d.CreatedAtUtc,
            d.UpdatedAtUtc);

    private static ClientAccountConnectionData ToData(ClientAccountConnection connection)
    {
        var d = new ClientAccountConnectionData { Id = connection.Id.Value };
        MapToExisting(connection, d);
        d.CreatedAtUtc = connection.CreatedAtUtc;
        return d;
    }

    private static void MapToExisting(ClientAccountConnection connection, ClientAccountConnectionData d)
    {
        d.OrganizationId = connection.OrganizationId;
        d.ClientId = connection.ClientId.Value;
        d.SocialNetworkId = connection.SocialNetworkId.Value;
        d.OrganizationIntegrationId = connection.OrganizationIntegrationId.Value;
        d.ExternalAccountId = connection.ExternalAccountId;
        d.EncryptedAccessToken = connection.EncryptedAccessToken;
        d.TokenExpiresAtUtc = connection.TokenExpiresAtUtc;
        d.Status = (short)connection.Status;
        d.LastTestedAtUtc = connection.LastTestedAtUtc;
        d.LastError = connection.LastError;
        d.UpdatedAtUtc = connection.UpdatedAtUtc;
    }
}
