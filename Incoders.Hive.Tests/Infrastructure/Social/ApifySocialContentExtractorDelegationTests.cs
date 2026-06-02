using System.Net;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Social.Providers;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;

namespace Incoders.Hive.Tests.Infrastructure.Social;

public sealed class ApifySocialContentExtractorDelegationTests
{
    private static ResolvedProviderCredential BuildCredential(string platformKey) =>
        new(
            Guid.NewGuid(),
            "apify",
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string>
            {
                ["api_key"] = "test-token",
                ["actor_mappings_json"] =
                    $"[{{\"networkKey\":\"{platformKey}\",\"variantKey\":\"single-video\",\"actorId\":\"actor/{platformKey}\"}}]"
            },
            SelectedModel: null);

    // ─── TikTok adapter selected ────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_SelectsTikTokAdapter_ForTikTokPlatformKey()
    {
        var spyAdapter = new SpyAdapter("tiktok");
        var handler = new RecordingHandler("""[{"text":"test","authorMeta":{"nickName":"brand"},"videoMeta":{"duration":30,"coverUrl":"https://cdn.example.com/cover.jpg"},"playCount":1,"diggCount":1,"commentCount":0,"shareCount":0,"collectCount":0,"webVideoUrl":"https://www.tiktok.com/@brand/video/1","isSlideshow":false}]""");

        var extractor = new ApifySocialContentExtractor(
            BuildCredential("tiktok"),
            new StaticHttpClientFactory(handler),
            [spyAdapter, new DefaultApifyScrapeAdapter()]);

        await extractor.ExtractAsync(
            new SocialContentRequest(Guid.NewGuid(), "https://www.tiktok.com/@brand/video/1",
                "https://www.tiktok.com/@brand/video/1", "tiktok", "social-extraction"),
            CancellationToken.None);

        Assert.True(spyAdapter.BuildActorInputCalled, "TikTok adapter BuildActorInput was not called");
        Assert.True(spyAdapter.MapItemCalled, "TikTok adapter MapItem was not called");

        // Body sent to HTTP must NOT be the old {startUrls} shape
        Assert.NotNull(handler.LastRequestBody);
        Assert.DoesNotContain("startUrls", handler.LastRequestBody, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Default adapter fallback for unknown platform ──────────────────────

    [Fact]
    public async Task ExtractAsync_FallsBackToDefaultAdapter_ForUnknownPlatform()
    {
        var tikTokSpy = new SpyAdapter("tiktok");
        var defaultSpy = new SpyDefaultAdapter();
        var handler = new RecordingHandler("""[{"title":"fallback"}]""");

        var extractor = new ApifySocialContentExtractor(
            BuildCredential("youtube"), // youtube in actor_mappings but no youtube adapter
            new StaticHttpClientFactory(handler),
            [tikTokSpy, defaultSpy]);

        await extractor.ExtractAsync(
            new SocialContentRequest(Guid.NewGuid(), "https://www.youtube.com/watch?v=abc",
                "https://www.youtube.com/watch?v=abc", "youtube", "social-extraction"),
            CancellationToken.None);

        Assert.False(tikTokSpy.BuildActorInputCalled, "TikTok adapter should not have been called for youtube");
        Assert.True(defaultSpy.BuildActorInputCalled, "Default adapter BuildActorInput should have been called");
    }

    // ─── YouTube guard: never route a single URL to the channel shorts-scraper ──

    [Fact]
    public async Task ExtractAsync_RoutesYouTubeToGenericActor_WhenShortsScraperConfiguredFirst()
    {
        // Both actors configured (shorts-scraper listed FIRST), both with the default variant —
        // exactly how the integrations UI persists them. A single Shorts/video URL must still hit
        // the generic youtube-scraper, because youtube-shorts-scraper only accepts a "channels" input.
        var credential = new ResolvedProviderCredential(
            Guid.NewGuid(),
            "apify",
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string>
            {
                ["api_key"] = "test-token",
                ["actor_mappings_json"] =
                    "[{\"networkKey\":\"youtube\",\"variantKey\":\"default\",\"actorId\":\"streamers/youtube-shorts-scraper\"}," +
                    "{\"networkKey\":\"youtube\",\"variantKey\":\"default\",\"actorId\":\"streamers/youtube-scraper\"}]"
            },
            SelectedModel: null);

        var handler = new RecordingHandler("""[{"title":"a short","type":"shorts","url":"https://www.youtube.com/shorts/abc"}]""");
        var extractor = new ApifySocialContentExtractor(
            credential,
            new StaticHttpClientFactory(handler),
            [new YouTubeApifyScrapeAdapter(), new DefaultApifyScrapeAdapter()]);

        var result = await extractor.ExtractAsync(
            new SocialContentRequest(Guid.NewGuid(), "https://www.youtube.com/shorts/abc",
                "https://www.youtube.com/shorts/abc", "youtube", "social-extraction"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(handler.LastRequestUri);
        // The actor id travels in the request path (URL-encoded). The generic actor must be targeted,
        // never the channel-based shorts-scraper.
        Assert.Contains("youtube-scraper", handler.LastRequestUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("youtube-shorts-scraper", handler.LastRequestUri, StringComparison.OrdinalIgnoreCase);
    }

    // ─── No actor ID → ProviderCredentialUnavailable ────────────────────────

    [Fact]
    public async Task ExtractAsync_ReturnsFailure_WhenActorIdIsNull()
    {
        var credential = new ResolvedProviderCredential(
            Guid.NewGuid(),
            "apify",
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "test-token" }, // no actor_mappings_json, no actor_id
            SelectedModel: null);

        var handler = new RecordingHandler("[]");
        var extractor = new ApifySocialContentExtractor(
            credential,
            new StaticHttpClientFactory(handler),
            [new DefaultApifyScrapeAdapter()]);

        var result = await extractor.ExtractAsync(
            new SocialContentRequest(Guid.NewGuid(), "https://www.tiktok.com/@brand/video/1",
                "https://www.tiktok.com/@brand/video/1", "tiktok", "social-extraction"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderCredentialUnavailable.Code, result.Error.Code);
    }

    // ─── Test infrastructure ─────────────────────────────────────────────────

    private sealed class SpyAdapter : ISocialPlatformScrapeAdapter
    {
        public SpyAdapter(string platformKey) => PlatformKey = platformKey;

        public string PlatformKey { get; }
        public bool BuildActorInputCalled { get; private set; }
        public bool MapItemCalled { get; private set; }

        public object BuildActorInput(SocialContentRequest request)
        {
            BuildActorInputCalled = true;
            return new { postURLs = new[] { request.NormalizedUrl }, resultsPerPage = 1 };
        }

        public SocialContentSnapshot MapItem(JsonElement item, SocialContentRequest request)
        {
            MapItemCalled = true;
            return new SocialContentSnapshot("tiktok", request.NormalizedUrl, null, null, "spy", null, null,
                new Dictionary<string, long>(), null, [], null, [], [], null, null);
        }
    }

    private sealed class SpyDefaultAdapter : ISocialPlatformScrapeAdapter
    {
        public string PlatformKey => "default";
        public bool BuildActorInputCalled { get; private set; }

        public object BuildActorInput(SocialContentRequest request)
        {
            BuildActorInputCalled = true;
            return new { startUrls = new[] { new { url = request.NormalizedUrl } } };
        }

        public SocialContentSnapshot MapItem(JsonElement item, SocialContentRequest request) =>
            new("youtube", request.NormalizedUrl, null, null, null, null, null,
                new Dictionary<string, long>(), null, [], null, [], [], null, null);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;
        public string? LastRequestBody { get; private set; }
        public string? LastRequestUri { get; private set; }

        public RecordingHandler(string response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();

            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            };
        }
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StaticHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
