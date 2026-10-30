using System.Net;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using SocialStudio.Infrastructure.Social.ContentGeneration;

namespace SocialStudio.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>Phase 2 (CAG2-PA-02): archiver downloads + re-hosts post images; best-effort per image.</summary>
public sealed class SupabasePostMediaArchiverTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();
    private static readonly Guid Analysis = Guid.NewGuid();

    [Fact]
    public async Task ArchivesEachImage_UnderAestheticPath_WithDurableRefs()
    {
        var storage = new RecordingStorage();
        var archiver = new SupabasePostMediaArchiver(new StubHttpFactory(new OkHandler()), storage);

        var refs = await archiver.ArchiveAsync(Org, Client, Analysis, "p1",
            ["https://cdn/cover.jpg", "https://cdn/child.png"], CancellationToken.None);

        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => Assert.Equal("p1", r.SourcePostId));
        Assert.Contains(refs, r => r.StoragePath.Contains("aesthetic/p1-0", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(refs, r => r.StoragePath.Contains("aesthetic/p1-1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, storage.PutCount);
    }

    [Fact]
    public async Task DownloadFailure_SkipsThatImage_KeepsTheRest()
    {
        var storage = new RecordingStorage();
        // First URL 404s, second succeeds.
        var archiver = new SupabasePostMediaArchiver(new StubHttpFactory(new SelectiveHandler()), storage);

        var refs = await archiver.ArchiveAsync(Org, Client, Analysis, "p1",
            ["https://cdn/missing.jpg", "https://cdn/ok.jpg"], CancellationToken.None);

        Assert.Single(refs);
        Assert.Equal(1, storage.PutCount);
    }

    [Fact]
    public async Task NoUrls_ReturnsEmpty_NoStorageCalls()
    {
        var storage = new RecordingStorage();
        var archiver = new SupabasePostMediaArchiver(new StubHttpFactory(new OkHandler()), storage);

        var refs = await archiver.ArchiveAsync(Org, Client, Analysis, "p1", [], CancellationToken.None);

        Assert.Empty(refs);
        Assert.Equal(0, storage.PutCount);
    }
}

file sealed class RecordingStorage : IGeneratedAssetStorage
{
    public int PutCount { get; private set; }

    public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
    {
        PutCount++;
        var path = $"{key.OrganizationId}/{key.ClientId}/{key.AnalysisId}/{key.RelativePath}";
        return Task.FromResult(new StoredAssetRef(path, content.Mime, content.Bytes.Length, "hash-" + PutCount));
    }

    public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
    public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct) =>
        Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
    public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
}

file sealed class OkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) });
}

file sealed class SelectiveHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var ok = request.RequestUri!.AbsoluteUri.Contains("ok", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(new HttpResponseMessage(ok ? HttpStatusCode.OK : HttpStatusCode.NotFound)
        {
            Content = new ByteArrayContent(ok ? [1, 2, 3] : []),
        });
    }
}

file sealed class StubHttpFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public StubHttpFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
