using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfOrganizationIntegrationRepository : IOrganizationIntegrationRepository, IApifyIntegrationScanner
{
    private readonly AppIdentityDbContext _dbContext;

    public EfOrganizationIntegrationRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OrganizationIntegration?> GetByOrganizationAndProviderAsync(
        Guid organizationId,
        IntegrationProviderId providerId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.OrganizationIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.ProviderId == providerId.Value,
                cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.OrganizationIntegrations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<OrganizationIntegration>> ListByOrganizationAndCategoryAsync(
        Guid organizationId,
        IntegrationProviderCategory category,
        CancellationToken cancellationToken = default)
    {
        // Categories are persisted as a JSON array of enum names (e.g. ["AiModel","MediaGeneration"]).
        // Match the quoted token so a provider that serves the category is included even when it also
        // serves others (one shared connector listed under multiple sections).
        var categoryToken = $"\"{category}\"";

        var rows = await _dbContext.OrganizationIntegrations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Join(
                _dbContext.IntegrationProviders.AsNoTracking(),
                integration => integration.ProviderId,
                provider => provider.Id,
                (integration, provider) => new { integration, provider })
            .Where(x => x.provider.CategoriesJson.Contains(categoryToken))
            .Select(x => x.integration)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToList();
    }

    public Task AddAsync(OrganizationIntegration integration, CancellationToken cancellationToken = default)
    {
        _dbContext.OrganizationIntegrations.Add(ToData(integration));
        return Task.CompletedTask;
    }

    public void Update(OrganizationIntegration integration)
    {
        var entity = _dbContext.OrganizationIntegrations.Local.FirstOrDefault(x => x.Id == integration.Id.Value)
                     ?? _dbContext.OrganizationIntegrations.First(x => x.Id == integration.Id.Value);

        entity.ConnectionMode = integration.ConnectionMode.ToString();
        entity.Status = integration.Status.ToString();
        entity.IsReadOnly = integration.IsReadOnly;
        entity.SelectedAiModel = integration.SelectedAiModel;
        entity.LastTestedAtUtc = integration.LastTestedAtUtc;
        entity.LastError = integration.LastError;
        entity.UpdatedAtUtc = integration.UpdatedAtUtc;
    }

    public void Remove(OrganizationIntegration integration)
    {
        var entity = _dbContext.OrganizationIntegrations.Local.FirstOrDefault(x => x.Id == integration.Id.Value)
                     ?? _dbContext.OrganizationIntegrations.First(x => x.Id == integration.Id.Value);

        _dbContext.OrganizationIntegrations.Remove(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrganizationIntegrationId>> ListApifyIntegrationIdsAsync(
        IntegrationProviderId providerId,
        CancellationToken cancellationToken = default)
    {
        var ids = await _dbContext.OrganizationIntegrations
            .AsNoTracking()
            .Where(x => x.ProviderId == providerId.Value)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        return ids.Select(id => new OrganizationIntegrationId(id)).ToList();
    }

    private static OrganizationIntegration ToDomain(OrganizationIntegrationData row) =>
        OrganizationIntegration.Restore(
            new OrganizationIntegrationId(row.Id),
            row.OrganizationId,
            new IntegrationProviderId(row.ProviderId),
            Enum.Parse<IntegrationConnectionMode>(row.ConnectionMode),
            Enum.Parse<IntegrationStatus>(row.Status),
            row.IsReadOnly,
            row.SelectedAiModel,
            row.LastTestedAtUtc,
            row.LastError,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);

    private static OrganizationIntegrationData ToData(OrganizationIntegration integration) =>
        new()
        {
            Id = integration.Id.Value,
            OrganizationId = integration.OrganizationId,
            ProviderId = integration.ProviderId.Value,
            ConnectionMode = integration.ConnectionMode.ToString(),
            Status = integration.Status.ToString(),
            IsReadOnly = integration.IsReadOnly,
            SelectedAiModel = integration.SelectedAiModel,
            LastTestedAtUtc = integration.LastTestedAtUtc,
            LastError = integration.LastError,
            CreatedAtUtc = integration.CreatedAtUtc,
            UpdatedAtUtc = integration.UpdatedAtUtc,
        };
}