using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.Clients.Delete;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Application.Social.Clients;

public sealed class DeleteSocialClientHandlerTests
{
    [Fact]
    public async Task Handle_WhenClientExists_DeletesDatabaseRowsThenCleansStorageArtifacts()
    {
        var clientId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var deleteResult = new SocialClientHardDeleteResult(
            ClientId: clientId,
            OrganizationId: organizationId,
            ClientName: "Garbage client",
            GeneratedPackageStorageUris: ["file:///tmp/package.zip"],
            GeneratedAssetStoragePaths: ["org/client/analysis/story.png"]);
        var repository = new FakeHardDeleteRepository(deleteResult);
        var packageStorage = new SpyGeneratedPackageStorage();
        var assetStorage = new SpyGeneratedAssetStorage();
        var clientAssetStorage = new SpyClientAssetStorage();
        var unitOfWork = new SpyUnitOfWork();

        var handler = new DeleteSocialClientHandler(
            repository,
            packageStorage,
            assetStorage,
            clientAssetStorage,
            unitOfWork,
            NullLogger<DeleteSocialClientHandler>.Instance);

        var result = await handler.Handle(new DeleteSocialClientCommand(clientId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Garbage client", result.Value.Name);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
        Assert.Equal(["file:///tmp/package.zip"], packageStorage.DeletedUris);
        Assert.Equal(["org/client/analysis/story.png"], assetStorage.DeletedPaths);
        Assert.Equal([(organizationId, clientId)], clientAssetStorage.DeletedClientFolders);
    }

    [Fact]
    public async Task Handle_WhenClientDoesNotExist_ReturnsNotFoundAndDoesNotCleanStorage()
    {
        var handler = new DeleteSocialClientHandler(
            new FakeHardDeleteRepository(null),
            new SpyGeneratedPackageStorage(),
            new SpyGeneratedAssetStorage(),
            new SpyClientAssetStorage(),
            new SpyUnitOfWork(),
            NullLogger<DeleteSocialClientHandler>.Instance);

        var result = await handler.Handle(new DeleteSocialClientCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("social.clients.not_found", result.Error.Code);
    }

    private sealed class FakeHardDeleteRepository : ISocialClientHardDeleteRepository
    {
        private readonly SocialClientHardDeleteResult? _result;

        public FakeHardDeleteRepository(SocialClientHardDeleteResult? result)
        {
            _result = result;
        }

        public Task<SocialClientHardDeleteResult?> HardDeleteAsync(
            Guid clientId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result is not null && _result.ClientId == clientId ? _result : null);
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
        public List<string> DeletedPaths { get; } = [];

        public Task<StoredAssetRef> PutAssetAsync(
            StoredAssetKey key,
            StoredAssetContent content,
            CancellationToken ct) =>
            Task.FromResult(new StoredAssetRef("unused", content.Mime, content.Bytes.LongLength, "hash"));

        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);

        public Task<SocialStudio.Domain.Common.Result<StoredAssetStream>> OpenAsync(
            string storagePath,
            CancellationToken ct) =>
            Task.FromResult(SocialStudio.Domain.Common.Result.Failure<StoredAssetStream>(StorageErrors.NotFound));

        public Task DeleteAsync(string storagePath, CancellationToken ct)
        {
            DeletedPaths.Add(storagePath);
            return Task.CompletedTask;
        }
    }

    private sealed class SpyClientAssetStorage : IClientAssetStorage
    {
        public List<(Guid OrganizationId, Guid ClientId)> DeletedClientFolders { get; } = [];

        public Task<SocialStudio.Domain.Common.Result<ClientAssetUploadResult>> UploadImageAsync(
            ClientAssetUploadRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SocialStudio.Domain.Common.Result.Failure<ClientAssetUploadResult>(
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
