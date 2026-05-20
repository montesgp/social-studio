using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfSocialClientRepository : ISocialClientRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfSocialClientRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Client?> GetByIdAsync(
        ClientId id,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SocialClients
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == id.Value && x.OrganizationId == organizationId,
                cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public async Task<(IReadOnlyList<Client> Items, int TotalCount)> ListAsync(
        SocialClientQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SocialClientData> source = _dbContext.SocialClients
            .AsNoTracking();

        if (query.OrganizationId.HasValue)
        {
            source = source.Where(x => x.OrganizationId == query.OrganizationId.Value);
        }

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

    public Task AddAsync(Client client, CancellationToken cancellationToken = default)
    {
        _dbContext.SocialClients.Add(ToData(client));
        return Task.CompletedTask;
    }

    public void Update(Client client)
    {
        var entity = _dbContext.SocialClients.Local.FirstOrDefault(x => x.Id == client.Id.Value)
                     ?? _dbContext.SocialClients.First(x => x.Id == client.Id.Value);

        entity.Name = client.Name;
        entity.Description = client.Description;
        entity.IsActive = client.IsActive;
        entity.UpdatedAtUtc = client.UpdatedAtUtc;
    }

    private static Client ToDomain(SocialClientData row) =>
        Client.Restore(
            new ClientId(row.Id),
            row.OrganizationId,
            row.Name,
            row.Description,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static SocialClientData ToData(Client client) =>
        new()
        {
            Id = client.Id.Value,
            OrganizationId = client.OrganizationId,
            Name = client.Name,
            Description = client.Description,
            IsActive = client.IsActive,
            CreatedAtUtc = client.CreatedAtUtc,
            UpdatedAtUtc = client.UpdatedAtUtc,
        };
}
