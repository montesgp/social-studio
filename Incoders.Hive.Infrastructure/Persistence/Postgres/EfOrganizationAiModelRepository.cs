using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfOrganizationAiModelRepository : IOrganizationAiModelRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfOrganizationAiModelRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<OrganizationAiModelRecord>> ListByProviderAsync(
        Guid organizationId,
        Guid providerId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.OrganizationAiModels
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ProviderId == providerId && x.IsAvailable)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return rows.Select(ToRecord).ToList();
    }

    public async Task UpsertManyAsync(
        IReadOnlyList<OrganizationAiModelRecord> models,
        CancellationToken cancellationToken = default)
    {
        if (models.Count == 0)
        {
            return;
        }

        var organizationId = models[0].OrganizationId;
        var providerId = models[0].ProviderId;
        var modelIds = models.Select(x => x.ModelId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = await _dbContext.OrganizationAiModels
            .Where(x => x.OrganizationId == organizationId && x.ProviderId == providerId)
            .ToListAsync(cancellationToken);

        foreach (var row in existing.Where(x => !modelIds.Contains(x.ModelId)))
        {
            row.IsAvailable = false;
            row.UpdatedAtUtc = models[0].UpdatedAtUtc;
        }

        foreach (var model in models)
        {
            var row = existing.FirstOrDefault(x =>
                string.Equals(x.ModelId, model.ModelId, StringComparison.OrdinalIgnoreCase));

            if (row is null)
            {
                _dbContext.OrganizationAiModels.Add(ToData(model));
                continue;
            }

            row.OrganizationIntegrationId = model.OrganizationIntegrationId;
            row.ProviderKey = model.ProviderKey;
            row.DisplayName = model.DisplayName;
            row.CapabilitiesJson = model.CapabilitiesJson;
            row.MetadataJson = model.MetadataJson;
            row.IsAvailable = model.IsAvailable;
            row.SyncedAtUtc = model.SyncedAtUtc;
            row.UpdatedAtUtc = model.UpdatedAtUtc;
        }
    }

    private static OrganizationAiModelRecord ToRecord(OrganizationAiModelData row) =>
        new(
            row.Id,
            row.OrganizationId,
            row.OrganizationIntegrationId,
            row.ProviderId,
            row.ProviderKey,
            row.ModelId,
            row.DisplayName,
            row.CapabilitiesJson,
            row.MetadataJson,
            row.IsAvailable,
            row.SyncedAtUtc,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static OrganizationAiModelData ToData(OrganizationAiModelRecord row) =>
        new()
        {
            Id = row.Id,
            OrganizationId = row.OrganizationId,
            OrganizationIntegrationId = row.OrganizationIntegrationId,
            ProviderId = row.ProviderId,
            ProviderKey = row.ProviderKey,
            ModelId = row.ModelId,
            DisplayName = row.DisplayName,
            CapabilitiesJson = row.CapabilitiesJson,
            MetadataJson = row.MetadataJson,
            IsAvailable = row.IsAvailable,
            SyncedAtUtc = row.SyncedAtUtc,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
        };
}

