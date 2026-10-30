using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class EfOrganizationIntegrationCredentialRepository : IOrganizationIntegrationCredentialRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfOrganizationIntegrationCredentialRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<OrganizationIntegrationCredential>> ListByIntegrationIdAsync(
        OrganizationIntegrationId integrationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.OrganizationIntegrationCredentials
            .AsNoTracking()
            .Where(x => x.OrganizationIntegrationId == integrationId.Value)
            .OrderBy(x => x.Key)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToList();
    }

    public async Task UpsertAsync(OrganizationIntegrationCredential credential, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.OrganizationIntegrationCredentials
            .FirstOrDefaultAsync(
                x => x.OrganizationIntegrationId == credential.OrganizationIntegrationId.Value
                     && x.Key == credential.Key,
                cancellationToken);

        if (existing is null)
        {
            _dbContext.OrganizationIntegrationCredentials.Add(ToData(credential));
            return;
        }

        existing.EncryptedValue = credential.EncryptedValue;
        existing.UpdatedAtUtc = credential.UpdatedAtUtc;
    }

    public Task RemoveByIntegrationIdAsync(
        OrganizationIntegrationId integrationId,
        CancellationToken cancellationToken = default)
    {
        var rows = _dbContext.OrganizationIntegrationCredentials
            .Where(x => x.OrganizationIntegrationId == integrationId.Value)
            .ToList();

        if (rows.Count > 0)
        {
            _dbContext.OrganizationIntegrationCredentials.RemoveRange(rows);
        }

        return Task.CompletedTask;
    }

    private static OrganizationIntegrationCredential ToDomain(OrganizationIntegrationCredentialData row) =>
        OrganizationIntegrationCredential.Restore(
            new OrganizationIntegrationCredentialId(row.Id),
            new OrganizationIntegrationId(row.OrganizationIntegrationId),
            row.Key,
            row.EncryptedValue,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static OrganizationIntegrationCredentialData ToData(OrganizationIntegrationCredential credential) =>
        new()
        {
            Id = credential.Id.Value,
            OrganizationIntegrationId = credential.OrganizationIntegrationId.Value,
            Key = credential.Key,
            EncryptedValue = credential.EncryptedValue,
            CreatedAtUtc = credential.CreatedAtUtc,
            UpdatedAtUtc = credential.UpdatedAtUtc,
        };
}