using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Infrastructure.Social;
using Incoders.Hive.Tests.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Tests.Infrastructure.Social;

public sealed class LocalClientAssetStorageTests : IDisposable
{
    private readonly string _tempRoot;

    public LocalClientAssetStorageTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "incoders-hive-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task UploadImageAsync_WithValidImage_PersistsFileAndReturnsPublicPath()
    {
        var clock = new TestClock(new DateTime(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc));
        var storage = CreateStorage(clock);
        var organizationId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        await using var content = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });

        var result = await storage.UploadImageAsync(new ClientAssetUploadRequest(
            OrganizationId: organizationId,
            ClientId: clientId,
            ContentType: "image/png",
            OriginalFileName: "logo.png",
            Content: content,
            SizeBytes: content.Length));

        Assert.True(result.IsSuccess);
        Assert.StartsWith("/storage/client-assets/", result.Value.PublicPath);
        Assert.Equal("image/png", result.Value.ContentType);
        Assert.Equal(content.Length, result.Value.SizeBytes);

        var persistedRelativePath = result.Value.PublicPath.Replace("/storage/client-assets/", string.Empty);
        var persistedAbsolutePath = Path.Combine(_tempRoot, persistedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(persistedAbsolutePath));
    }

    [Fact]
    public async Task UploadImageAsync_WithUnsupportedContentType_ReturnsValidationFailure()
    {
        var storage = CreateStorage(new TestClock(DateTime.UtcNow));
        await using var content = new MemoryStream(new byte[] { 9, 9, 9 });

        var result = await storage.UploadImageAsync(new ClientAssetUploadRequest(
            OrganizationId: Guid.NewGuid(),
            ClientId: Guid.NewGuid(),
            ContentType: "application/pdf",
            OriginalFileName: "manual.pdf",
            Content: content,
            SizeBytes: content.Length));

        Assert.True(result.IsFailure);
        Assert.Equal(ClientAssetStorageErrors.InvalidContentType.Code, result.Error.Code);
    }

    [Fact]
    public async Task UploadImageAsync_WhenFileExceedsLimit_ReturnsValidationFailure()
    {
        var options = new ClientAssetStorageOptions
        {
            RootPath = _tempRoot,
            PublicBasePath = "/storage/client-assets",
            MaxImageSizeBytes = 2,
            AllowedImageContentTypes = ["image/png"],
        };

        var storage = new LocalClientAssetStorage(
            Options.Create(options),
            new FakeHostEnvironment(_tempRoot),
            new TestClock(DateTime.UtcNow));

        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await storage.UploadImageAsync(new ClientAssetUploadRequest(
            OrganizationId: Guid.NewGuid(),
            ClientId: Guid.NewGuid(),
            ContentType: "image/png",
            OriginalFileName: "oversize.png",
            Content: content,
            SizeBytes: content.Length));

        Assert.True(result.IsFailure);
        Assert.Equal(ClientAssetStorageErrors.FileTooLarge.Code, result.Error.Code);
    }

    [Fact]
    public async Task DeleteClientAssetsAsync_RemovesOnlyTheRequestedClientFolder()
    {
        var clock = new TestClock(new DateTime(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc));
        var storage = CreateStorage(clock);
        var organizationId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var otherClientId = Guid.NewGuid();

        await UploadPngAsync(storage, organizationId, clientId);
        await UploadPngAsync(storage, organizationId, otherClientId);

        var clientFolder = Path.Combine(_tempRoot, organizationId.ToString("N"), clientId.ToString("N"));
        var otherClientFolder = Path.Combine(_tempRoot, organizationId.ToString("N"), otherClientId.ToString("N"));
        Assert.True(Directory.Exists(clientFolder));
        Assert.True(Directory.Exists(otherClientFolder));

        await storage.DeleteClientAssetsAsync(organizationId, clientId);

        Assert.False(Directory.Exists(clientFolder));
        Assert.True(Directory.Exists(otherClientFolder));
    }

    private LocalClientAssetStorage CreateStorage(TestClock clock)
    {
        var options = new ClientAssetStorageOptions
        {
            RootPath = _tempRoot,
            PublicBasePath = "/storage/client-assets",
            MaxImageSizeBytes = 8 * 1024 * 1024,
            AllowedImageContentTypes = ["image/png", "image/jpeg", "image/jpg", "image/webp", "image/gif"],
        };

        return new LocalClientAssetStorage(
            Options.Create(options),
            new FakeHostEnvironment(_tempRoot),
            clock);
    }

    private static async Task UploadPngAsync(
        LocalClientAssetStorage storage,
        Guid organizationId,
        Guid clientId)
    {
        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await storage.UploadImageAsync(new ClientAssetUploadRequest(
            OrganizationId: organizationId,
            ClientId: clientId,
            ContentType: "image/png",
            OriginalFileName: "logo.png",
            Content: content,
            SizeBytes: content.Length));

        Assert.True(result.IsSuccess);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ApplicationName = "Incoders.Hive.Tests";
            EnvironmentName = "Test";
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; }

        public string ContentRootPath { get; set; }

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
