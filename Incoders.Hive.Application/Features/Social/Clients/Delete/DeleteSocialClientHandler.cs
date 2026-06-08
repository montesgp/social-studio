using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Incoders.Hive.Application.Features.Social.Clients.Delete;

/// <summary>
/// Temporary MVP hard-delete use case for platform sysadmins.
/// Normal organization admins must keep using archive/reactivate.
/// </summary>
public sealed class DeleteSocialClientHandler
    : IRequestHandler<DeleteSocialClientCommand, Result<DeletedSocialClientResponse>>
{
    private readonly ISocialClientHardDeleteRepository _clients;
    private readonly IGeneratedPackageStorage _packageStorage;
    private readonly IGeneratedAssetStorage _assetStorage;
    private readonly IClientAssetStorage _clientAssetStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteSocialClientHandler> _logger;

    public DeleteSocialClientHandler(
        ISocialClientHardDeleteRepository clients,
        IGeneratedPackageStorage packageStorage,
        IGeneratedAssetStorage assetStorage,
        IClientAssetStorage clientAssetStorage,
        IUnitOfWork unitOfWork,
        ILogger<DeleteSocialClientHandler> logger)
    {
        _clients = clients;
        _packageStorage = packageStorage;
        _assetStorage = assetStorage;
        _clientAssetStorage = clientAssetStorage;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<DeletedSocialClientResponse>> Handle(
        DeleteSocialClientCommand request,
        CancellationToken cancellationToken)
    {
        var deleteResult = await _clients.HardDeleteAsync(request.Id, cancellationToken);
        if (deleteResult is null)
        {
            return Result.Failure<DeletedSocialClientResponse>(ClientErrors.NotFound);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await DeleteGeneratedPackagesBestEffortAsync(deleteResult, cancellationToken);
        await DeleteGeneratedAssetsBestEffortAsync(deleteResult, cancellationToken);
        await DeleteClientAssetFolderBestEffortAsync(deleteResult, cancellationToken);

        return Result.Success(new DeletedSocialClientResponse(
            deleteResult.ClientId,
            deleteResult.OrganizationId,
            deleteResult.ClientName));
    }

    private async Task DeleteGeneratedPackagesBestEffortAsync(
        SocialClientHardDeleteResult deleteResult,
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
                    "Failed to delete generated package '{StorageUri}' while hard-deleting client {ClientId}. " +
                    "Database rows were deleted successfully; the file is now orphaned.",
                    uri,
                    deleteResult.ClientId);
            }
        }
    }

    private async Task DeleteGeneratedAssetsBestEffortAsync(
        SocialClientHardDeleteResult deleteResult,
        CancellationToken cancellationToken)
    {
        var storagePaths = deleteResult.GeneratedAssetStoragePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var clientPrefix = $"{deleteResult.OrganizationId}/{deleteResult.ClientId}/";
            var clientObjects = await _assetStorage.ListAsync(clientPrefix, cancellationToken);

            foreach (var clientObject in clientObjects)
            {
                if (!string.IsNullOrWhiteSpace(clientObject.StoragePath))
                {
                    storagePaths.Add(clientObject.StoragePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to list generated assets while hard-deleting client {ClientId}. " +
                "Continuing with database-known asset rows only.",
                deleteResult.ClientId);
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
                    "Failed to delete generated asset '{StoragePath}' while hard-deleting client {ClientId}. " +
                    "Database rows were deleted successfully; the file is now orphaned.",
                    storagePath,
                    deleteResult.ClientId);
            }
        }
    }

    private async Task DeleteClientAssetFolderBestEffortAsync(
        SocialClientHardDeleteResult deleteResult,
        CancellationToken cancellationToken)
    {
        try
        {
            await _clientAssetStorage.DeleteClientAssetsAsync(
                deleteResult.OrganizationId,
                deleteResult.ClientId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete uploaded client asset folder for client {ClientId}. " +
                "Database rows were deleted successfully; the files are now orphaned.",
                deleteResult.ClientId);
        }
    }
}
