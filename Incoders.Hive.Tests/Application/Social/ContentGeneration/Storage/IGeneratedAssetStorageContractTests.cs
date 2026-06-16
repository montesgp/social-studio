using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Storage;

/// <summary>
/// T3.1 — Compile-guard tests verifying that the <see cref="IGeneratedAssetStorage"/> port
/// and its supporting types compile and satisfy the expected contract without any Infrastructure
/// type leaking into the Application layer.
/// </summary>
public sealed class IGeneratedAssetStorageContractTests
{
    [Fact]
    public void StoredAssetKey_CanBeConstructed()
    {
        var key = new StoredAssetKey(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "story.png");

        Assert.NotEqual(Guid.Empty, key.OrganizationId);
        Assert.Equal("story.png", key.RelativePath);
    }

    [Fact]
    public void StoredAssetKey_CarouselSlide_RelativePathContainsSubfolder()
    {
        var key = new StoredAssetKey(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "carousel/slide_1.png");

        Assert.Contains("/", key.RelativePath);
        Assert.StartsWith("carousel/", key.RelativePath);
    }

    [Fact]
    public void StoredAssetContent_CanBeConstructed()
    {
        var content = new StoredAssetContent([1, 2, 3], "image/png");

        Assert.Equal("image/png", content.Mime);
        Assert.Equal(3, content.Bytes.Length);
    }

    [Fact]
    public void StoredAssetRef_CanBeConstructed()
    {
        var storagePath = "org-id/client-id/analysis-id/flyer.png";
        var assetRef = new StoredAssetRef(storagePath, "image/png", 1024L, "abc123hash");

        Assert.Equal(storagePath, assetRef.StoragePath);
        Assert.Equal(1024L, assetRef.SizeBytes);
        Assert.Equal("abc123hash", assetRef.ContentHash);
    }

    [Fact]
    public void StoredAssetEntry_CanBeConstructed()
    {
        var entry = new StoredAssetEntry("org/client/analysis/flyer.png", "story.png", 2048, "image/png", DateTimeOffset.UtcNow, false);

        Assert.False(entry.IsFolder);
        Assert.Equal("story.png", entry.Name);
    }

    [Fact]
    public void StoredAssetStream_Dispose_DisposesInnerStream()
    {
        var ms = new MemoryStream([1, 2, 3]);
        var assetStream = new StoredAssetStream(ms, "image/png", 3);

        assetStream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => ms.ReadByte());
    }

    [Fact]
    public void StorageErrors_NotFound_HasCorrectType()
    {
        Assert.Equal(ErrorType.NotFound, StorageErrors.NotFound.Type);
    }

    [Fact]
    public void StorageErrors_UploadFailed_HasFailureType()
    {
        Assert.Equal(ErrorType.Failure, StorageErrors.UploadFailed.Type);
    }

    [Fact]
    public void IGeneratedAssetStorage_InterfaceExistsInApplicationLayer()
    {
        // Compile-time assertion: the interface lives in the Application assembly, not Infrastructure.
        var type = typeof(IGeneratedAssetStorage);
        Assert.True(type.IsInterface);
        Assert.Contains("Application", type.Assembly.FullName ?? string.Empty);
    }
}
