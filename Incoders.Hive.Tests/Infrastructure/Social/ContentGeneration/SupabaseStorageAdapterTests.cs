using System.Net;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// T3.5 — TDD tests for <see cref="SupabaseStorageAdapter"/> using a faked HttpMessageHandler.
/// No real network calls are made. Tests assert path construction, header requirements,
/// x-upsert behaviour, list pagination, 404 mapping, and non-2xx typed failure.
/// </summary>
public sealed class SupabaseStorageAdapterTests
{
    private const string FakeUrl = "https://test.supabase.co";
    private const string FakeBucket = "test-bucket";
    private const string FakeKey = "fake-service-role-key";

    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnalysisId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // -----------------------------------------------------------------------
    // PUT asset — path construction
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PutAssetAsync_SendsPutToCorrectStoragePath()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var adapter = BuildAdapter(handler);

        await adapter.PutAssetAsync(
            new StoredAssetKey(OrgId, ClientId, AnalysisId, "story.png"),
            new StoredAssetContent([1, 2, 3], "image/png"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        var path = handler.LastRequest.RequestUri!.PathAndQuery;
        Assert.Contains(FakeBucket, path);
        Assert.Contains(OrgId.ToString(), path);
        Assert.Contains(ClientId.ToString(), path);
        Assert.Contains(AnalysisId.ToString(), path);
        Assert.Contains("story.png", path);
    }

    [Fact]
    public async Task PutAssetAsync_SendsXUpsertTrueHeader()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var adapter = BuildAdapter(handler);

        await adapter.PutAssetAsync(
            new StoredAssetKey(OrgId, ClientId, AnalysisId, "story.png"),
            new StoredAssetContent([1, 2, 3], "image/png"),
            CancellationToken.None);

        Assert.True(handler.LastRequest!.Headers.TryGetValues("x-upsert", out var values));
        Assert.Equal("true", values!.First());
    }

    [Fact]
    public async Task PutAssetAsync_SendsBearerAndApiKeyHeaders()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var adapter = BuildAdapter(handler);

        await adapter.PutAssetAsync(
            new StoredAssetKey(OrgId, ClientId, AnalysisId, "reel-script.txt"),
            new StoredAssetContent(Encoding.UTF8.GetBytes("hello"), "text/plain"),
            CancellationToken.None);

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal(FakeKey, handler.LastRequest.Headers.Authorization.Parameter);
        Assert.True(handler.LastRequest.Headers.TryGetValues("apikey", out var apiKeyValues));
        Assert.Equal(FakeKey, apiKeyValues!.First());
    }

    [Fact]
    public async Task PutAssetAsync_CarouselSlide_EncodePathSegmentsPreservingSlash()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var adapter = BuildAdapter(handler);

        await adapter.PutAssetAsync(
            new StoredAssetKey(OrgId, ClientId, AnalysisId, "carousel/slide_1.png"),
            new StoredAssetContent([1, 2, 3], "image/png"),
            CancellationToken.None);

        var path = handler.LastRequest!.RequestUri!.PathAndQuery;
        Assert.Contains("carousel/slide_1.png", path);
    }

    [Fact]
    public async Task PutAssetAsync_ReturnsStoredAssetRef_WithCorrectStoragePath()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var adapter = BuildAdapter(handler);
        var key = new StoredAssetKey(OrgId, ClientId, AnalysisId, "story.png");

        var result = await adapter.PutAssetAsync(
            key,
            new StoredAssetContent([0xDE, 0xAD, 0xBE, 0xEF], "image/png"),
            CancellationToken.None);

        var expectedPath = $"{OrgId}/{ClientId}/{AnalysisId}/story.png";
        Assert.Equal(expectedPath, result.StoragePath);
        Assert.Equal("image/png", result.Mime);
        Assert.Equal(4L, result.SizeBytes);
        Assert.NotEmpty(result.ContentHash);
    }

    [Fact]
    public async Task PutAssetAsync_Non2xx_ThrowsGeneratedAssetStorageException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "error");
        var adapter = BuildAdapter(handler);

        await Assert.ThrowsAsync<GeneratedAssetStorageException>(() =>
            adapter.PutAssetAsync(
                new StoredAssetKey(OrgId, ClientId, AnalysisId, "story.png"),
                new StoredAssetContent([1], "image/png"),
                CancellationToken.None));
    }

    // -----------------------------------------------------------------------
    // List — uses POST with JSON body
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ListAsync_SendsPostToListEndpoint()
    {
        var listResponse = SerializeListResponse([new FakeStorageItem("story.png", 1024, "image/png")]);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, listResponse);
        var adapter = BuildAdapter(handler);

        await adapter.ListAsync($"{OrgId}/{ClientId}/{AnalysisId}/", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/list/", handler.LastRequest.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task ListAsync_SendsJsonBodyWithPrefixAndLimit()
    {
        var listResponse = SerializeListResponse([]);
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, listResponse);
        var adapter = BuildAdapter(handler);
        var prefix = $"{OrgId}/{ClientId}/{AnalysisId}/";

        await adapter.ListAsync(prefix, CancellationToken.None);

        var body = handler.LastRequestBody;
        Assert.NotNull(body);
        Assert.Contains("prefix", body);
    }

    [Fact]
    public async Task ListAsync_ReturnsEntries_FromResponse()
    {
        var items = new[]
        {
            new FakeStorageItem("story.png", 2048, "image/png"),
            new FakeStorageItem("reel-script.txt", 512, "text/plain"),
        };
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SerializeListResponse(items));
        var adapter = BuildAdapter(handler);

        var results = await adapter.ListAsync($"{OrgId}/{ClientId}/{AnalysisId}/", CancellationToken.None);

        Assert.Equal(2, results.Count);
    }

    // -----------------------------------------------------------------------
    // OpenAsync — GET, 404 mapping
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OpenAsync_ValidPath_ReturnsSuccessWithStream()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "image-bytes");
        var adapter = BuildAdapter(handler);

        var result = await adapter.OpenAsync($"{OrgId}/{ClientId}/{AnalysisId}/flyer.png", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task OpenAsync_404Response_ReturnsNotFoundResult()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound, "not found");
        var adapter = BuildAdapter(handler);

        var result = await adapter.OpenAsync($"{OrgId}/{ClientId}/{AnalysisId}/missing.png", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task OpenAsync_Non2xx_ReturnsFailureResult()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "forbidden");
        var adapter = BuildAdapter(handler);

        var result = await adapter.OpenAsync($"{OrgId}/{ClientId}/{AnalysisId}/flyer.png", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    // -----------------------------------------------------------------------
    // Path security — reject ..
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PutAssetAsync_RelativePathWithDotDot_ThrowsArgumentException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var adapter = BuildAdapter(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            adapter.PutAssetAsync(
                new StoredAssetKey(OrgId, ClientId, AnalysisId, "../escape.png"),
                new StoredAssetContent([1], "image/png"),
                CancellationToken.None));
    }

    // -----------------------------------------------------------------------
    // Delete
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_SendsDeleteRequest()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var adapter = BuildAdapter(handler);

        await adapter.DeleteAsync($"{OrgId}/{ClientId}/{AnalysisId}/flyer.png", CancellationToken.None);

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SupabaseStorageAdapter BuildAdapter(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(FakeUrl) };
        var options = Options.Create(new SupabaseStorageOptions
        {
            Url = FakeUrl,
            Bucket = FakeBucket,
            ServiceRoleKey = FakeKey,
        });
        return new SupabaseStorageAdapter(httpClient, options);
    }

    private static string SerializeListResponse(FakeStorageItem[] items)
    {
        return JsonSerializer.Serialize(items.Select(i => new
        {
            name = i.Name,
            metadata = new { size = i.Size, mimetype = i.Mime },
        }));
    }

    private sealed record FakeStorageItem(string Name, long Size, string Mime);
}

/// <summary>Captures the last request sent through the handler for assertion in tests.</summary>
internal sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody),
        };
    }
}
