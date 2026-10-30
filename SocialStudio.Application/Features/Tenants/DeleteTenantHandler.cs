using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Tenants;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Application.Features.Tenants;

/// <summary>
/// Temporary MVP hard-delete use case for platform sysadmins.
/// Normal organization lifecycle must continue to use archive/reactivate.
/// </summary>
public sealed class DeleteTenantHandler
    : IRequestHandler<DeleteTenantCommand, Result<DeletedTenantResponse>>
{
    private readonly ITenantHardDeleteRepository _tenants;
    private readonly IGeneratedPackageStorage _packageStorage;
    private readonly IGeneratedAssetStorage _assetStorage;
    private readonly IClientAssetStorage _clientAssetStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteTenantHandler> _logger;

    public DeleteTenantHandler(
        ITenantHardDeleteRepository tenants,
        IGeneratedPackageStorage packageStorage,
        IGeneratedAssetStorage assetStorage,
        IClientAssetStorage clientAssetStorage,
        IUnitOfWork unitOfWork,
        ILogger<DeleteTenantHandler> logger)
    {
        _tenants = tenants;
        _packageStorage = packageStorage;
        _assetStorage = assetStorage;
        _clientAssetStorage = clientAssetStorage;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<DeletedTenantResponse>> Handle(
        DeleteTenantCommand request,
        CancellationToken cancellationToken)
    {
        var deleteResult = await _tenants.HardDeleteAsync(request.Id, cancellationToken);
        if (deleteResult is null)
        {
            return Result.Failure<DeletedTenantResponse>(TenantErrors.NotFound);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await DeleteGeneratedPackagesBestEffortAsync(deleteResult, cancellationToken);
        await DeleteGeneratedAssetsBestEffortAsync(deleteResult, cancellationToken);
        await DeleteClientAssetFoldersBestEffortAsync(deleteResult, cancellationToken);

        return Result.Success(new DeletedTenantResponse(
            deleteResult.OrganizationId,
            deleteResult.OrganizationName,
            deleteResult.ClientIds.Count,
            deleteResult.UserIds.Count));
    }

    private async Task DeleteGeneratedPackagesBestEffortAsync(
        TenantHardDeleteResult deleteResult,
        CancellationToken cancellationToken)
    {
        foreach (var uri in deleteResult.GeneratedPackageStorageUris)
        {
            try
            {
                await _packageStorage.DeleteAsync(new StoredPackageRef(uri), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete generated package '{StorageUri}' while hard-deleting organization {OrganizationId}. " +
                    "Database rows were deleted successfully; the file is now orphaned.",
                    uri,
                    deleteResult.OrganizationId);
            }
        }
    }

    private async Task DeleteGeneratedAssetsBestEffortAsync(
        TenantHardDeleteResult deleteResult,
        CancellationToken cancellationToken)
    {
        var storagePaths = deleteResult.GeneratedAssetStoragePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var organizationPrefix = $"{deleteResult.OrganizationId}/";
            var organizationObjects = await _assetStorage.ListAsync(organizationPrefix, cancellationToken);

            foreach (var organizationObject in organizationObjects)
            {
                if (!string.IsNullOrWhiteSpace(organizationObject.StoragePath))
                {
                    storagePaths.Add(organizationObject.StoragePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to list generated assets while hard-deleting organization {OrganizationId}. " +
                "Continuing with database-known asset rows only.",
                deleteResult.OrganizationId);
        }

        foreach (var storagePath in storagePaths)
        {
            try
            {
                await _assetStorage.DeleteAsync(storagePath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete generated asset '{StoragePath}' while hard-deleting organization {OrganizationId}. " +
                    "Database rows were deleted successfully; the file is now orphaned.",
                    storagePath,
                    deleteResult.OrganizationId);
            }
        }
    }

    private async Task DeleteClientAssetFoldersBestEffortAsync(
        TenantHardDeleteResult deleteResult,
        CancellationToken cancellationToken)
    {
        foreach (var clientId in deleteResult.ClientIds)
        {
            try
            {
                await _clientAssetStorage.DeleteClientAssetsAsync(
                    deleteResult.OrganizationId,
                    clientId,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete uploaded client asset folder for client {ClientId} while hard-deleting organization {OrganizationId}. " +
                    "Database rows were deleted successfully; the files are now orphaned.",
                    clientId,
                    deleteResult.OrganizationId);
            }
        }
    }
}
