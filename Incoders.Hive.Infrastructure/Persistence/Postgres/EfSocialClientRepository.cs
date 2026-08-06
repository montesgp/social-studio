using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Social.Clients;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.Infrastructure.Persistence.Postgres;

public sealed class EfSocialClientRepository
    : ISocialClientRepository, ISocialClientHardDeleteRepository, ISocialClientDirectoryRepository
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

    public async Task<Client?> GetByIdAcrossOrganizationsAsync(
        ClientId id,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SocialClients
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);

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
        entity.SectorOrNiche = client.SectorOrNiche;
        entity.IsActive = client.IsActive;
        entity.UpdatedAtUtc = client.UpdatedAtUtc;
        entity.BackgroundSourcingPolicy = client.BackgroundSourcingPolicy is { } policy ? (short)policy : null;
        // CAG-CS-01: explicit assignment required — the predecessor bug was a silent drop here.
        entity.AestheticRefreshThresholdDays = client.AestheticRefreshThresholdDays is { } t ? (short)t : null;
        entity.MaxPostsToAnalyze = client.MaxPostsToAnalyze is { } mp ? (short)mp : null;
        entity.SignsOwnSlides = client.SignsOwnSlides;
    }

    public async Task<SocialClientHardDeleteResult?> HardDeleteAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var client = await _dbContext.SocialClients
            .FirstOrDefaultAsync(x => x.Id == clientId, cancellationToken);

        if (client is null)
        {
            return null;
        }

        var contentPlanIds = await _dbContext.ContentPlans
            .Where(x => x.ClientId == clientId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var analysisIds = await _dbContext.SingleContentAnalyses
            .Where(x => x.ClientId == clientId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var analysisIdsWithOtherClientJobs = analysisIds.Count == 0
            ? []
            : await _dbContext.ContentGenerationJobs
                .Where(j =>
                    analysisIds.Contains(j.AnalysisId)
                    && j.ClientId.HasValue
                    && j.ClientId.Value != clientId)
                .Select(j => j.AnalysisId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var analysisIdsToKeep = analysisIdsWithOtherClientJobs.ToHashSet();
        var analysisIdsToDelete = analysisIds
            .Where(id => !analysisIdsToKeep.Contains(id))
            .ToList();

        var clientScopedJobIds = await _dbContext.ContentGenerationJobs
            .Where(j => j.ClientId == clientId)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        var analysisOwnedJobIds = analysisIdsToDelete.Count == 0
            ? []
            : await _dbContext.ContentGenerationJobs
                .Where(j => analysisIdsToDelete.Contains(j.AnalysisId))
                .Select(j => j.Id)
                .ToListAsync(cancellationToken);

        var jobIdsToDelete = clientScopedJobIds
            .Concat(analysisOwnedJobIds)
            .Distinct()
            .ToList();

        var formatResultIdsToDelete = jobIdsToDelete.Count == 0
            ? []
            : await _dbContext.ContentGenerationFormatResults
                .Where(x => jobIdsToDelete.Contains(x.JobId))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

        var generatedPackageStorageUris = jobIdsToDelete.Count == 0
            ? []
            : await _dbContext.GeneratedPackages
                .Where(x => jobIdsToDelete.Contains(x.JobId))
                .Select(x => x.StorageUri)
                .Where(uri => uri != string.Empty)
                .ToListAsync(cancellationToken);

        var generatedAssetStoragePaths = formatResultIdsToDelete.Count == 0
            ? []
            : await _dbContext.ContentGenerationFormatAssets
                .Where(x => formatResultIdsToDelete.Contains(x.FormatResultId))
                .Select(x => x.StoragePath)
                .Where(path => path != string.Empty)
                .ToListAsync(cancellationToken);

        if (formatResultIdsToDelete.Count > 0)
        {
            _dbContext.ContentGenerationFormatAssets.RemoveRange(
                await _dbContext.ContentGenerationFormatAssets
                    .Where(x => formatResultIdsToDelete.Contains(x.FormatResultId))
                    .ToListAsync(cancellationToken));
        }

        if (jobIdsToDelete.Count > 0)
        {
            _dbContext.GeneratedPackages.RemoveRange(
                await _dbContext.GeneratedPackages
                    .Where(x => jobIdsToDelete.Contains(x.JobId))
                    .ToListAsync(cancellationToken));

            _dbContext.ContentGenerationFormatResults.RemoveRange(
                await _dbContext.ContentGenerationFormatResults
                    .Where(x => jobIdsToDelete.Contains(x.JobId))
                    .ToListAsync(cancellationToken));

            _dbContext.ContentGenerationJobs.RemoveRange(
                await _dbContext.ContentGenerationJobs
                    .Where(x => jobIdsToDelete.Contains(x.Id))
                    .ToListAsync(cancellationToken));
        }

        if (analysisIdsToDelete.Count > 0)
        {
            _dbContext.IntegrationExecutionAttempts.RemoveRange(
                await _dbContext.IntegrationExecutionAttempts
                    .Where(x => x.AnalysisId.HasValue && analysisIdsToDelete.Contains(x.AnalysisId.Value))
                    .ToListAsync(cancellationToken));

            _dbContext.SingleContentAnalyses.RemoveRange(
                await _dbContext.SingleContentAnalyses
                    .Where(x => analysisIdsToDelete.Contains(x.Id))
                    .ToListAsync(cancellationToken));
        }

        if (analysisIdsToKeep.Count > 0)
        {
            var sharedAnalyses = await _dbContext.SingleContentAnalyses
                .Where(x => analysisIdsWithOtherClientJobs.Contains(x.Id))
                .ToListAsync(cancellationToken);

            foreach (var analysis in sharedAnalyses)
            {
                analysis.ClientId = null;
            }
        }

        if (contentPlanIds.Count > 0)
        {
            _dbContext.ContentPlanSlots.RemoveRange(
                await _dbContext.ContentPlanSlots
                    .Where(x => contentPlanIds.Contains(x.ContentPlanId))
                    .ToListAsync(cancellationToken));
        }

        _dbContext.ContentPlans.RemoveRange(
            await _dbContext.ContentPlans
                .Where(x => x.ClientId == clientId)
                .ToListAsync(cancellationToken));
        _dbContext.GeneralStrategies.RemoveRange(
            await _dbContext.GeneralStrategies
                .Where(x => x.ClientId == clientId)
                .ToListAsync(cancellationToken));
        _dbContext.ClientBrandManuals.RemoveRange(
            await _dbContext.ClientBrandManuals
                .Where(x => x.ClientId == clientId)
                .ToListAsync(cancellationToken));
        _dbContext.ClientReferentAccounts.RemoveRange(
            await _dbContext.ClientReferentAccounts
                .Where(x => x.ClientId == clientId)
                .ToListAsync(cancellationToken));
        _dbContext.ClientCompetitorAccounts.RemoveRange(
            await _dbContext.ClientCompetitorAccounts
                .Where(x => x.ClientId == clientId)
                .ToListAsync(cancellationToken));
        _dbContext.ClientOwnAccounts.RemoveRange(
            await _dbContext.ClientOwnAccounts
                .Where(x => x.ClientId == clientId)
                .ToListAsync(cancellationToken));
        _dbContext.ProfileAnalyses.RemoveRange(
            await _dbContext.ProfileAnalyses
                .Where(x => x.ClientId == clientId)
                .ToListAsync(cancellationToken));
        _dbContext.UsageEvents.RemoveRange(
            await _dbContext.UsageEvents
                .Where(x => x.ClientId == clientId)
                .ToListAsync(cancellationToken));

        _dbContext.SocialClients.Remove(client);

        return new SocialClientHardDeleteResult(
            client.Id,
            client.OrganizationId,
            client.Name,
            generatedPackageStorageUris,
            generatedAssetStoragePaths);
    }

    private static Client ToDomain(SocialClientData row) =>
        Client.Restore(
            new ClientId(row.Id),
            row.OrganizationId,
            row.Name,
            row.Description,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.SectorOrNiche,
            row.BackgroundSourcingPolicy is { } policy ? (BackgroundSourcingPolicy)policy : null,
            row.AestheticRefreshThresholdDays is { } t ? (int)t : null,
            row.MaxPostsToAnalyze is { } mp ? (int)mp : null,
            row.SignsOwnSlides);

    private static SocialClientData ToData(Client client) =>
        new()
        {
            Id = client.Id.Value,
            OrganizationId = client.OrganizationId,
            Name = client.Name,
            Description = client.Description,
            SectorOrNiche = client.SectorOrNiche,
            IsActive = client.IsActive,
            CreatedAtUtc = client.CreatedAtUtc,
            UpdatedAtUtc = client.UpdatedAtUtc,
            BackgroundSourcingPolicy = client.BackgroundSourcingPolicy is { } policy ? (short)policy : null,
            AestheticRefreshThresholdDays = client.AestheticRefreshThresholdDays is { } t ? (short)t : null,
            MaxPostsToAnalyze = client.MaxPostsToAnalyze is { } mp ? (short)mp : null,
            SignsOwnSlides = client.SignsOwnSlides,
        };
}
