using System.Text;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using SocialStudio.Infrastructure.Social.ContentGeneration;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// TDD tests for <see cref="LocalFileGeneratedAssetStorage"/> — the dev-local filesystem
/// implementation of <see cref="IGeneratedAssetStorage"/> that replaces Supabase Storage in
/// local development so iteration consumes ZERO cloud egress/storage.
/// <para>
/// Storage-path format MUST be byte-for-byte identical to <c>SupabaseStorageAdapter.BuildStoragePath</c>:
/// "{organizationId}/{clientId}/{analysisId}/{relativePath}" (forward slashes).
/// </para>
/// </summary>
public sealed class LocalFileGeneratedAssetStorageTests : IDisposable
{
    private readonly string _root;

    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnalysisId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public LocalFileGeneratedAssetStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hive-local-asset-storage-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private LocalFileGeneratedAssetStorage BuildStorage() => new(_root);

    // -----------------------------------------------------------------------
    // Round-trip: PutAssetAsync -> OpenAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PutAssetAsync_ThenOpenAsync_RoundTripsBytesAndMime()
    {
        var storage = BuildStorage();
        var key = new StoredAssetKey(OrgId, ClientId, AnalysisId, "story.png");
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var putResult = await storage.PutAssetAsync(key, new StoredAssetContent(bytes, "image/png"), CancellationToken.None);

        var expectedPath = $"{OrgId}/{ClientId}/{AnalysisId}/story.png";
        Assert.Equal(expectedPath, putResult.StoragePath);
        Assert.Equal("image/png", putResult.Mime);
        Assert.Equal(4L, putResult.SizeBytes);
        Assert.NotEmpty(putResult.ContentHash);

        var openResult = await storage.OpenAsync(expectedPath, CancellationToken.None);
        Assert.True(openResult.IsSuccess);

        using var stream = openResult.Value;
        Assert.Equal("image/png", stream.Mime);
        Assert.Equal(4L, stream.SizeBytes);

        using var ms = new MemoryStream();
        await stream.Content.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());
    }

    [Fact]
    public async Task PutAssetAsync_CarouselSlide_RoundTripsUnderSubfolder()
    {
        var storage = BuildStorage();
        var key = new StoredAssetKey(OrgId, ClientId, AnalysisId, "carousel/slide_1.png");
        var bytes = new byte[] { 1, 2, 3 };

        var putResult = await storage.PutAssetAsync(key, new StoredAssetContent(bytes, "image/png"), CancellationToken.None);

        var expectedPath = $"{OrgId}/{ClientId}/{AnalysisId}/carousel/slide_1.png";
        Assert.Equal(expectedPath, putResult.StoragePath);

        var openResult = await storage.OpenAsync(expectedPath, CancellationToken.None);
        Assert.True(openResult.IsSuccess);
        openResult.Value.Dispose();
    }

    // -----------------------------------------------------------------------
    // Missing path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OpenAsync_MissingPath_ReturnsFailureNotFound()
    {
        var storage = BuildStorage();

        var result = await storage.OpenAsync($"{OrgId}/{ClientId}/{AnalysisId}/missing.png", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // -----------------------------------------------------------------------
    // ListAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_ReturnsEntriesUnderPrefix()
    {
        var storage = BuildStorage();
        var prefix = $"{OrgId}/{ClientId}/{AnalysisId}/";

        await storage.PutAssetAsync(
            new StoredAssetKey(OrgId, ClientId, AnalysisId, "story.png"),
            new StoredAssetContent([1], "image/png"),
            CancellationToken.None);
        await storage.PutAssetAsync(
            new StoredAssetKey(OrgId, ClientId, AnalysisId, "reel-script.txt"),
            new StoredAssetContent(Encoding.UTF8.GetBytes("hi"), "text/plain"),
            CancellationToken.None);
        // A different analysis must not show up under this prefix.
        await storage.PutAssetAsync(
            new StoredAssetKey(OrgId, ClientId, Guid.NewGuid(), "other.png"),
            new StoredAssetContent([1], "image/png"),
            CancellationToken.None);

        var entries = await storage.ListAsync(prefix, CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.StartsWith(prefix, e.StoragePath, StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------------
    // DeleteAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_RemovesFile_SubsequentOpenReturnsNotFound()
    {
        var storage = BuildStorage();
        var key = new StoredAssetKey(OrgId, ClientId, AnalysisId, "story.png");
        var putResult = await storage.PutAssetAsync(key, new StoredAssetContent([1, 2, 3], "image/png"), CancellationToken.None);

        await storage.DeleteAsync(putResult.StoragePath, CancellationToken.None);

        var result = await storage.OpenAsync(putResult.StoragePath, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task DeleteAsync_AbsentPath_DoesNotThrow()
    {
        var storage = BuildStorage();

        var exception = await Record.ExceptionAsync(() =>
            storage.DeleteAsync($"{OrgId}/{ClientId}/{AnalysisId}/never-existed.png", CancellationToken.None));

        Assert.Null(exception);
    }

    // -----------------------------------------------------------------------
    // Path traversal — must be rejected / contained
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PutAssetAsync_RelativePathWithDotDot_ThrowsArgumentException()
    {
        var storage = BuildStorage();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.PutAssetAsync(
                new StoredAssetKey(OrgId, ClientId, AnalysisId, "../../evil.png"),
                new StoredAssetContent([1], "image/png"),
                CancellationToken.None));

        var escapedFile = Path.Combine(Path.GetTempPath(), "hive-local-asset-storage-tests", "evil.png");
        Assert.False(File.Exists(escapedFile));
    }

    [Fact]
    public async Task OpenAsync_PathTraversalAttempt_ReturnsNotFound_DoesNotThrow()
    {
        var storage = BuildStorage();

        var result = await storage.OpenAsync("../../../../etc/passwd", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task DeleteAsync_PathTraversalAttempt_DoesNotThrow()
    {
        var storage = BuildStorage();

        var exception = await Record.ExceptionAsync(() =>
            storage.DeleteAsync("../../../../etc/passwd", CancellationToken.None));

        Assert.Null(exception);
    }

    // -----------------------------------------------------------------------
    // Folder placeholder
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PutFolderPlaceholderAsync_CreatesDirectory()
    {
        var storage = BuildStorage();
        var folderPath = $"{OrgId}/{ClientId}/{AnalysisId}/carousel";

        await storage.PutFolderPlaceholderAsync(folderPath, CancellationToken.None);

        var expectedDir = Path.Combine(_root, OrgId.ToString(), ClientId.ToString(), AnalysisId.ToString(), "carousel");
        Assert.True(Directory.Exists(expectedDir));
    }

    // -----------------------------------------------------------------------
    // Content hash must match SupabaseStorageAdapter's algorithm: SHA-256 hex, lowercase.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PutAssetAsync_ContentHash_MatchesSha256HexLowercase()
    {
        var storage = BuildStorage();
        var bytes = Encoding.UTF8.GetBytes("hive-asset-storage-hash-check");

        var result = await storage.PutAssetAsync(
            new StoredAssetKey(OrgId, ClientId, AnalysisId, "hash-check.txt"),
            new StoredAssetContent(bytes, "text/plain"),
            CancellationToken.None);

        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(expectedHash, result.ContentHash);
    }
}
