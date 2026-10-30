using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Tenancy;

public sealed class EfTenantHardDeleteRepository : ITenantHardDeleteRepository
{
    private readonly AppIdentityDbContext _dbContext;

    public EfTenantHardDeleteRepository(AppIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantHardDeleteResult?> HardDeleteAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var organization = await _dbContext.Tenants
            .FirstOrDefaultAsync(x => x.Id == organizationId, cancellationToken);

        if (organization is null)
        {
            return null;
        }

        var clientIds = await _dbContext.SocialClients
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var membershipUserIds = await _dbContext.TenantMemberships
            .Where(x => x.TenantId == organizationId)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);

        var candidateUserIds = membershipUserIds
            .Append(organization.OwnerUserId)
            .Distinct()
            .ToList();

        var sysadminUserIds = candidateUserIds.Count == 0
            ? []
            : await _dbContext.UserRoles
                .Where(x => candidateUserIds.Contains(x.UserId))
                .Join(
                    _dbContext.Roles,
                    userRole => userRole.RoleId,
                    role => role.Id,
                    (userRole, role) => new { userRole.UserId, role.Name, role.NormalizedName })
                .Where(x =>
                    x.Name == IdentityRoles.Sysadmin
                    || x.NormalizedName == IdentityRoles.Sysadmin.ToUpperInvariant())
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var sysadminUserIdSet = sysadminUserIds.ToHashSet();
        var userIdsToDelete = candidateUserIds
            .Where(userId => !sysadminUserIdSet.Contains(userId))
            .Distinct()
            .ToList();

        var integrationIds = await _dbContext.OrganizationIntegrations
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var analysisIds = await _dbContext.SingleContentAnalyses
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var contentPlanIds = await _dbContext.ContentPlans
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var jobIds = await _dbContext.ContentGenerationJobs
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var formatResultIds = jobIds.Count == 0
            ? []
            : await _dbContext.ContentGenerationFormatResults
                .Where(x => jobIds.Contains(x.JobId))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

        var generatedPackageStorageUris = jobIds.Count == 0
            ? []
            : await _dbContext.GeneratedPackages
                .Where(x => jobIds.Contains(x.JobId))
                .Select(x => x.StorageUri)
                .Where(uri => uri != string.Empty)
                .Distinct()
                .ToListAsync(cancellationToken);

        var generatedAssetStoragePaths = formatResultIds.Count == 0
            ? []
            : await _dbContext.ContentGenerationFormatAssets
                .Where(x => formatResultIds.Contains(x.FormatResultId))
                .Select(x => x.StoragePath)
                .Where(path => path != string.Empty)
                .Distinct()
                .ToListAsync(cancellationToken);

        if (formatResultIds.Count > 0)
        {
            await RemoveRangeAsync(
                _dbContext.ContentGenerationFormatAssets
                    .Where(x => formatResultIds.Contains(x.FormatResultId)),
                cancellationToken);
        }

        if (jobIds.Count > 0)
        {
            await RemoveRangeAsync(
                _dbContext.GeneratedPackages
                    .Where(x => jobIds.Contains(x.JobId)),
                cancellationToken);
            await RemoveRangeAsync(
                _dbContext.ContentGenerationFormatResults
                    .Where(x => jobIds.Contains(x.JobId)),
                cancellationToken);
            await RemoveRangeAsync(
                _dbContext.ContentGenerationJobs
                    .Where(x => jobIds.Contains(x.Id)),
                cancellationToken);
        }

        if (analysisIds.Count > 0)
        {
            await RemoveRangeAsync(
                _dbContext.IntegrationExecutionAttempts
                    .Where(x => x.AnalysisId.HasValue && analysisIds.Contains(x.AnalysisId.Value)),
                cancellationToken);
        }

        await RemoveRangeAsync(
            _dbContext.SingleContentAnalyses
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);
        await RemoveRangeAsync(
            _dbContext.ProfileAnalyses
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);
        await RemoveRangeAsync(
            _dbContext.UsageEvents
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);

        if (contentPlanIds.Count > 0)
        {
            await RemoveRangeAsync(
                _dbContext.ContentPlanSlots
                    .Where(x => contentPlanIds.Contains(x.ContentPlanId)),
                cancellationToken);
        }

        await RemoveRangeAsync(
            _dbContext.ContentPlans
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);
        await RemoveRangeAsync(
            _dbContext.GeneralStrategies
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);
        await RemoveRangeAsync(
            _dbContext.ClientBrandManuals
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);
        await RemoveRangeAsync(
            _dbContext.ClientReferentAccounts
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);
        await RemoveRangeAsync(
            _dbContext.ClientCompetitorAccounts
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);
        await RemoveRangeAsync(
            _dbContext.ClientOwnAccounts
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);

        if (integrationIds.Count > 0)
        {
            await RemoveRangeAsync(
                _dbContext.CanvaUserTokens
                    .Where(x => integrationIds.Contains(x.OrganizationIntegrationId)),
                cancellationToken);
            await RemoveRangeAsync(
                _dbContext.OrganizationIntegrationCredentials
                    .Where(x => integrationIds.Contains(x.OrganizationIntegrationId)),
                cancellationToken);
        }

        if (userIdsToDelete.Count > 0)
        {
            await RemoveRangeAsync(
                _dbContext.CanvaUserTokens
                    .Where(x => userIdsToDelete.Contains(x.UserId)),
                cancellationToken);
            await RemoveRangeAsync(
                _dbContext.SystemSettings
                    .Where(x => x.UserId.HasValue && userIdsToDelete.Contains(x.UserId.Value)),
                cancellationToken);
            await RemoveRangeAsync(
                _dbContext.UserPreferences
                    .Where(x => userIdsToDelete.Contains(x.UserId)),
                cancellationToken);
            await RemoveRangeAsync(
                _dbContext.UserClaims
                    .Where(x => userIdsToDelete.Contains(x.UserId)),
                cancellationToken);
            await RemoveRangeAsync(
                _dbContext.UserLogins
                    .Where(x => userIdsToDelete.Contains(x.UserId)),
                cancellationToken);
            await RemoveRangeAsync(
                _dbContext.UserTokens
                    .Where(x => userIdsToDelete.Contains(x.UserId)),
                cancellationToken);
            await RemoveRangeAsync(
                _dbContext.UserRoles
                    .Where(x => userIdsToDelete.Contains(x.UserId)),
                cancellationToken);
        }

        await RemoveRangeAsync(
            _dbContext.SystemSettings
                .Where(x => x.TenantId == organizationId),
            cancellationToken);
        await RemoveRangeAsync(
            _dbContext.OrganizationIntegrations
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);
        await RemoveRangeAsync(
            _dbContext.OrganizationUserModules
                .Where(x =>
                    x.OrganizationId == organizationId
                    || (userIdsToDelete.Count > 0 && userIdsToDelete.Contains(x.UserId))),
            cancellationToken);
        await RemoveRangeAsync(
            _dbContext.OrganizationModules
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);
        await RemoveRangeAsync(
            _dbContext.TenantMemberships
                .Where(x =>
                    x.TenantId == organizationId
                    || (userIdsToDelete.Count > 0 && userIdsToDelete.Contains(x.UserId))),
            cancellationToken);

        if (userIdsToDelete.Count > 0)
        {
            await RemoveRangeAsync(
                _dbContext.Users
                    .Where(x => userIdsToDelete.Contains(x.Id)),
                cancellationToken);
        }

        await RemoveRangeAsync(
            _dbContext.SocialClients
                .Where(x => x.OrganizationId == organizationId),
            cancellationToken);

        _dbContext.Tenants.Remove(organization);

        return new TenantHardDeleteResult(
            organization.Id,
            organization.Name,
            clientIds,
            userIdsToDelete,
            generatedPackageStorageUris,
            generatedAssetStoragePaths);
    }

    private async Task RemoveRangeAsync<TEntity>(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var rows = await query.ToListAsync(cancellationToken);
        if (rows.Count > 0)
        {
            _dbContext.RemoveRange(rows);
        }
    }
}
