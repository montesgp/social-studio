using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Tenants;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Tenants;

public sealed class DeleteTenantHandlerTests
{
    [Fact]
    public async Task Handle_WhenOrganizationExists_DeletesDatabaseRowsThenCleansStorageArtifacts()
    {
        var organizationId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var deleteResult = new TenantHardDeleteResult(
            OrganizationId: organizationId,
            OrganizationName: "Garbage organization",
            ClientIds: [clientId],
            UserIds: [userId],
            GeneratedPackageStorageUris: ["file:///tmp/org-package.zip"],
            GeneratedAssetStoragePaths: [$"{organizationId}/{clientId}/analysis/story.png"]);
        var repository = new FakeHardDeleteRepository(deleteResult);
        var packageStorage = new SpyGeneratedPackageStorage();
        var assetStorage = new SpyGeneratedAssetStorage(
            [new StoredAssetEntry($"{organizationId}/orphan/generated.png", "generated.png", 10, "image/png", null, false)]);
        var clientAssetStorage = new SpyClientAssetStorage();
        var unitOfWork = new SpyUnitOfWork();

        var handler = new DeleteTenantHandler(
            repository,
            packageStorage,
            assetStorage,
            clientAssetStorage,
            unitOfWork,
            NullLogger<DeleteTenantHandler>.Instance);

        var result = await handler.Handle(new DeleteTenantCommand(organizationId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Garbage organization", result.Value.Name);
        Assert.Equal(1, result.Value.DeletedClientCount);
        Assert.Equal(1, result.Value.DeletedUserCount);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
        Assert.Equal(["file:///tmp/org-package.zip"], packageStorage.DeletedUris);
        Assert.Equal($"{organizationId}/", assetStorage.ListedPrefixes.Single());
        Assert.Equal(
            [$"{organizationId}/{clientId}/analysis/story.png", $"{organizationId}/orphan/generated.png"],
            assetStorage.DeletedPaths.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal([(organizationId, clientId)], clientAssetStorage.DeletedClientFolders);
    }

    [Fact]
    public async Task Handle_WhenOrganizationDoesNotExist_ReturnsNotFoundAndDoesNotCleanStorage()
    {
        var packageStorage = new SpyGeneratedPackageStorage();
        var assetStorage = new SpyGeneratedAssetStorage([]);
        var clientAssetStorage = new SpyClientAssetStorage();
        var unitOfWork = new SpyUnitOfWork();
        var handler = new DeleteTenantHandler(
            new FakeHardDeleteRepository(null),
            packageStorage,
            assetStorage,
            clientAssetStorage,
            unitOfWork,
            NullLogger<DeleteTenantHandler>.Instance);

        var result = await handler.Handle(new DeleteTenantCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("tenants.not_found", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
        Assert.Empty(packageStorage.DeletedUris);
        Assert.Empty(assetStorage.DeletedPaths);
        Assert.Empty(clientAssetStorage.DeletedClientFolders);
    }

    private sealed class FakeHardDeleteRepository : ITenantHardDeleteRepository
    {
        private readonly TenantHardDeleteResult? _result;

        public FakeHardDeleteRepository(TenantHardDeleteResult? result)
        {
            _result = result;
        }

        public Task<TenantHardDeleteResult?> HardDeleteAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result is not null && _result.OrganizationId == organizationId ? _result : null);
    }

    private sealed class SpyUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return Task.FromResult(1);
        }
    }

    private sealed class SpyGeneratedPackageStorage : IGeneratedPackageStorage
    {
        public List<string> DeletedUris { get; } = [];

        public Task<StoredPackageRef> StoreAsync(Stream content, StorePackageKey key, CancellationToken ct) =>
            Task.FromResult(new StoredPackageRef("file:///unused.zip"));

        public Task<Stream> OpenAsync(StoredPackageRef refKey, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(StoredPackageRef refKey, CancellationToken ct)
        {
            DeletedUris.Add(refKey.Uri);
            return Task.CompletedTask;
        }
    }

    private sealed class SpyGeneratedAssetStorage : IGeneratedAssetStorage
    {
        private readonly IReadOnlyList<StoredAssetEntry> _listedEntries;

        public SpyGeneratedAssetStorage(IReadOnlyList<StoredAssetEntry> listedEntries)
        {
            _listedEntries = listedEntries;
        }

        public List<string> ListedPrefixes { get; } = [];

        public List<string> DeletedPaths { get; } = [];

        public Task<StoredAssetRef> PutAssetAsync(
            StoredAssetKey key,
            StoredAssetContent content,
            CancellationToken ct) =>
            Task.FromResult(new StoredAssetRef("unused", content.Mime, content.Bytes.LongLength, "hash"));

        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
        {
            ListedPrefixes.Add(prefix);
            return Task.FromResult(_listedEntries);
        }

        public Task<Incoders.Hive.Domain.Common.Result<StoredAssetStream>> OpenAsync(
            string storagePath,
            CancellationToken ct) =>
            Task.FromResult(Incoders.Hive.Domain.Common.Result.Failure<StoredAssetStream>(StorageErrors.NotFound));

        public Task DeleteAsync(string storagePath, CancellationToken ct)
        {
            DeletedPaths.Add(storagePath);
            return Task.CompletedTask;
        }
    }

    private sealed class SpyClientAssetStorage : IClientAssetStorage
    {
        public List<(Guid OrganizationId, Guid ClientId)> DeletedClientFolders { get; } = [];

        public Task<Incoders.Hive.Domain.Common.Result<ClientAssetUploadResult>> UploadImageAsync(
            ClientAssetUploadRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Incoders.Hive.Domain.Common.Result.Failure<ClientAssetUploadResult>(
                ClientAssetStorageErrors.InvalidContentType));

        public Task DeleteClientAssetsAsync(
            Guid organizationId,
            Guid clientId,
            CancellationToken cancellationToken = default)
        {
            DeletedClientFolders.Add((organizationId, clientId));
            return Task.CompletedTask;
        }
    }
}
