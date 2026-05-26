using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Infrastructure.Packaging;

namespace Incoders.Hive.Tests.Infrastructure.Packaging;

/// <summary>
/// TDD tests for <see cref="LocalGeneratedPackageStorage"/>.
/// </summary>
public sealed class LocalGeneratedPackageStorageTests : IDisposable
{
    private readonly string _tempRoot;

    public LocalGeneratedPackageStorageTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "incoders-hive-pkg-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task StoreAsync_ValidContent_ReturnsNonEmptyUri()
    {
        var storage = CreateStorage();
        var orgId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var content = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });

        var stored = await storage.StoreAsync(content, new StorePackageKey(orgId, jobId), CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(stored.Uri));
    }

    [Fact]
    public async Task StoreAsync_ThenOpenAsync_ReturnsSameBytes()
    {
        var storage = CreateStorage();
        var orgId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var original = new byte[] { 10, 20, 30, 40, 50 };

        var stored = await storage.StoreAsync(
            new MemoryStream(original),
            new StorePackageKey(orgId, jobId),
            CancellationToken.None);

        await using var readStream = await storage.OpenAsync(stored, CancellationToken.None);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);
        Assert.Equal(original, ms.ToArray());
    }

    [Fact]
    public async Task StoreAsync_PathContainsTenantAndJobKey()
    {
        var storage = CreateStorage();
        var orgId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var stored = await storage.StoreAsync(
            new MemoryStream(new byte[] { 1 }),
            new StorePackageKey(orgId, jobId),
            CancellationToken.None);

        Assert.Contains(orgId.ToString(), stored.Uri);
        Assert.Contains(jobId.ToString(), stored.Uri);
    }

    [Fact]
    public async Task DeleteAsync_RemovesUnderlyingFile()
    {
        var storage = CreateStorage();
        var stored = await storage.StoreAsync(
            new MemoryStream(new byte[] { 99 }),
            new StorePackageKey(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        // File should be accessible
        await using var s = await storage.OpenAsync(stored, CancellationToken.None);
        s.Close();

        await storage.DeleteAsync(stored, CancellationToken.None);

        // After delete, file should not exist → OpenAsync throws
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            storage.OpenAsync(stored, CancellationToken.None));
    }

    [Fact]
    public async Task StoreAsync_ConcurrentStoresForDifferentJobs_DoNotConflict()
    {
        var storage = CreateStorage();
        var orgId = Guid.NewGuid();

        var tasks = Enumerable.Range(0, 5).Select(i =>
            storage.StoreAsync(
                new MemoryStream([(byte)i]),
                new StorePackageKey(orgId, Guid.NewGuid()),
                CancellationToken.None));

        var results = await Task.WhenAll(tasks);
        var uris = results.Select(r => r.Uri).ToHashSet();
        Assert.Equal(5, uris.Count); // all unique
    }

    private LocalGeneratedPackageStorage CreateStorage() => new(_tempRoot);

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
