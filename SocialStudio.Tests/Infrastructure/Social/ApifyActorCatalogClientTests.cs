using System.Collections.Concurrent;
using System.Net;
using System.Text;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Social.Providers;

namespace SocialStudio.Tests.Infrastructure.Social;

/// <summary>
/// <c>ApifyActorCatalogClient</c> — the anti-corruption adapter over <c>GET /v2/acts</c>.
///
/// The endpoint returns every Actor the account created OR used. The <c>my</c> query parameter
/// restricts the result to owned Actors only and defaults to <c>false</c>; sending <c>my=1</c>
/// would drop the public-store Actors the organization routes to today (the TikTok route uses a
/// store actor the org does not own), so the client must never send it.
/// </summary>
public sealed class ApifyActorCatalogClientTests
{
    [Fact]
    public async Task ListActorsAsync_MapsRequiredFields_AndPrefersTitleAsDisplayName()
    {
        const string body = """
        {
            "data": {
                "total": 2,
                "count": 2,
                "offset": 0,
                "limit": 1000,
                "items": [
                    {
                        "id": "aBcDeFgHiJkLmNoP1",
                        "name": "my-scraper",
                        "username": "acme",
                        "title": "My Scraper",
                        "createdAt": "2026-01-01T00:00:00.000Z",
                        "modifiedAt": "2026-02-01T00:00:00.000Z"
                    },
                    {
                        "id": "zZ9YyXxWwVvUuTtS2",
                        "name": "tiktok-scraper",
                        "username": "vendor",
                        "createdAt": "2025-01-01T00:00:00.000Z",
                        "modifiedAt": "2025-02-01T00:00:00.000Z"
                    }
                ]
            }
        }
        """;

        var handler = new RecordingHandler(("acme", body));
        var result = await BuildClient(handler).ListActorsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);

        var owned = result.Value.Single(x => x.ActorId == "aBcDeFgHiJkLmNoP1");
        Assert.Equal("My Scraper", owned.DisplayName);
        Assert.Equal("my-scraper", owned.Name);
        Assert.Equal("acme", owned.Username);
        Assert.True(owned.IsOwned);

        // No title → fall back to the "username/name" slug, which is what the operator already
        // types into the connector today.
        var store = result.Value.Single(x => x.ActorId == "zZ9YyXxWwVvUuTtS2");
        Assert.Equal("vendor/tiktok-scraper", store.DisplayName);
        Assert.False(store.IsOwned);
    }

    [Fact]
    public async Task ListActorsAsync_NeverRestrictsToOwnedActors()
    {
        var handler = new RecordingHandler(("acme", EmptyPage));

        var result = await BuildClient(handler).ListActorsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var listRequests = handler.RequestUris.Where(x => x.AbsolutePath.Contains("/acts", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(listRequests);
        Assert.All(listRequests, uri => Assert.DoesNotContain("my=", uri.Query, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListActorsAsync_PaginatesUntilAShortPageIsReturned()
    {
        var firstPage = Page(Enumerable.Range(0, 1000).Select(i => $"id{i:D14}").ToArray(), offset: 0);
        var secondPage = Page(["tailActorId000001"], offset: 1000);
        var handler = new RecordingHandler(("acme", firstPage), ("acme", secondPage));

        var result = await BuildClient(handler).ListActorsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1001, result.Value.Count);

        var listRequests = handler.RequestUris.Where(x => x.AbsolutePath.Contains("/acts", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, listRequests.Count);
        Assert.Contains("offset=0", listRequests[0].Query, StringComparison.Ordinal);
        Assert.Contains("offset=1000", listRequests[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListActorsAsync_WithoutCredential_FailsWithCredentialUnavailable()
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            "apify",
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string>(),
            SelectedModel: null);

        var client = new ApifyActorCatalogClient(credential, new StaticHttpClientFactory(new RecordingHandler(("acme", EmptyPage))));
        var result = await client.ListActorsAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderCredentialUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task ListActorsAsync_Unauthorized_MapsToCredentialUnavailable()
    {
        var handler = new StatusHandler(HttpStatusCode.Unauthorized);
        var result = await BuildClient(handler).ListActorsAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderCredentialUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task ListActorsAsync_TransportFailure_MapsToProviderUnavailable()
    {
        var result = await BuildClient(new ThrowingHandler()).ListActorsAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ExternalProviderUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task ListActorsAsync_WhenIdentityLookupFails_StillReturnsActorsAsNotOwned()
    {
        var handler = new RecordingHandler(meBody: null, ("acme", Page(["aBcDeFgHiJkLmNoP1"], offset: 0)));

        var result = await BuildClient(handler).ListActorsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value, actor => Assert.False(actor.IsOwned));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private const string EmptyPage = """
    { "data": { "total": 0, "count": 0, "offset": 0, "limit": 1000, "items": [] } }
    """;

    private static string Page(IReadOnlyList<string> ids, int offset)
    {
        var items = string.Join(
            ",",
            ids.Select(id => $$"""
            {
                "id": "{{id}}",
                "name": "actor-{{id}}",
                "username": "acme",
                "createdAt": "2026-01-01T00:00:00.000Z",
                "modifiedAt": "2026-02-01T00:00:00.000Z"
            }
            """));

        return $$"""
        { "data": { "total": {{ids.Count}}, "count": {{ids.Count}}, "offset": {{offset}}, "limit": 1000, "items": [{{items}}] } }
        """;
    }

    private static ApifyActorCatalogClient BuildClient(HttpMessageHandler handler)
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            "apify",
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "test-token" },
            SelectedModel: null);

        return new ApifyActorCatalogClient(credential, new StaticHttpClientFactory(handler));
    }

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

    /// <summary>
    /// Answers <c>/v2/users/me</c> with the supplied username (or 401 when it is null) and serves
    /// the queued <c>/v2/acts</c> pages in order, recording every request URI.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string? _meBody;
        private readonly Queue<string> _pages;

        public RecordingHandler(params (string Username, string Body)[] pages)
            : this(pages.Length == 0 ? null : pages[0].Username, pages)
        {
        }

        public RecordingHandler(string? meBody, params (string Username, string Body)[] pages)
        {
            _meBody = meBody;
            _pages = new Queue<string>(pages.Select(x => x.Body));
        }

        public ConcurrentQueue<Uri> Recorded { get; } = new();

        public IReadOnlyList<Uri> RequestUris => [.. Recorded];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Recorded.Enqueue(uri);

            if (uri.AbsolutePath.Contains("/users/me", StringComparison.Ordinal))
            {
                return Task.FromResult(_meBody is null
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }
                    : Json($$"""{ "data": { "username": "{{_meBody}}" } }"""));
            }

            return Task.FromResult(Json(_pages.Count > 0 ? _pages.Dequeue() : EmptyPage));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StatusHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode) { Content = new StringContent("{}") });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StaticHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
