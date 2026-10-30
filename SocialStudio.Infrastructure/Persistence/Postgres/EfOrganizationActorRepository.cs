using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Persistence.Postgres;

public sealed class EfOrganizationActorRepository : IOrganizationActorRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfOrganizationActorRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<OrganizationActorRecord>> ListByProviderAsync(
        Guid organizationId,
        Guid providerId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.OrganizationActors
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ProviderId == providerId && x.IsAvailable)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return rows.Select(ToRecord).ToList();
    }

    public async Task UpsertManyAsync(
        IReadOnlyList<OrganizationActorRecord> actors,
        CancellationToken cancellationToken = default)
    {
        if (actors.Count == 0)
        {
            return;
        }

        var organizationId = actors[0].OrganizationId;
        var providerId = actors[0].ProviderId;
        var actorIds = actors.Select(x => x.ActorId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = await _dbContext.OrganizationActors
            .Where(x => x.OrganizationId == organizationId && x.ProviderId == providerId)
            .ToListAsync(cancellationToken);

        // Rows are retired, never deleted: a routing entry may still point at an actor that has
        // disappeared from the account, and the picker must keep showing it as the current value.
        foreach (var row in existing.Where(x => !actorIds.Contains(x.ActorId)))
        {
            row.IsAvailable = false;
            row.UpdatedAtUtc = actors[0].UpdatedAtUtc;
        }

        foreach (var actor in actors)
        {
            var row = existing.FirstOrDefault(x =>
                string.Equals(x.ActorId, actor.ActorId, StringComparison.OrdinalIgnoreCase));

            if (row is null)
            {
                _dbContext.OrganizationActors.Add(ToData(actor));
                continue;
            }

            row.OrganizationIntegrationId = actor.OrganizationIntegrationId;
            row.ProviderKey = actor.ProviderKey;
            row.Name = actor.Name;
            row.Username = actor.Username;
            row.DisplayName = actor.DisplayName;
            row.MetadataJson = actor.MetadataJson;
            row.IsOwned = actor.IsOwned;
            row.IsAvailable = actor.IsAvailable;
            row.SyncedAtUtc = actor.SyncedAtUtc;
            row.UpdatedAtUtc = actor.UpdatedAtUtc;
        }
    }

    private static OrganizationActorRecord ToRecord(OrganizationActorData row) =>
        new(
            row.Id,
            row.OrganizationId,
            row.OrganizationIntegrationId,
            row.ProviderId,
            row.ProviderKey,
            row.ActorId,
            row.Name,
            row.Username,
            row.DisplayName,
            row.MetadataJson,
            row.IsOwned,
            row.IsAvailable,
            row.SyncedAtUtc,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static OrganizationActorData ToData(OrganizationActorRecord row) =>
        new()
        {
            Id = row.Id,
            OrganizationId = row.OrganizationId,
            OrganizationIntegrationId = row.OrganizationIntegrationId,
            ProviderId = row.ProviderId,
            ProviderKey = row.ProviderKey,
            ActorId = row.ActorId,
            Name = row.Name,
            Username = row.Username,
            DisplayName = row.DisplayName,
            MetadataJson = row.MetadataJson,
            IsOwned = row.IsOwned,
            IsAvailable = row.IsAvailable,
            SyncedAtUtc = row.SyncedAtUtc,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
        };
}
