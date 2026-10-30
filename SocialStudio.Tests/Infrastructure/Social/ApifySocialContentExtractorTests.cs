using System.Net;
using System.Text.Json;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Social.Providers;
using SocialStudio.Infrastructure.Social.Providers.Apify;

namespace SocialStudio.Tests.Infrastructure.Social;

public sealed class ApifySocialContentExtractorTests
{
    [Fact]
    public async Task ExtractAsync_UsesActorMappingForRequestedNetwork()
    {
        var handler = new CapturingHandler();
        var extractor = new ApifySocialContentExtractor(
            new ResolvedProviderCredential(
                Guid.NewGuid(),
                "apify",
                IntegrationConnectionMode.BringYourOwnKey,
                new Dictionary<string, string>
                {
                    ["api_key"] = "test-token",
                    ["actor_mappings_json"] =
                        "[{\"networkKey\":\"tiktok\",\"variantKey\":\"social-extraction\",\"actorId\":\"clockworks/tiktok-scraper\",\"isPrimary\":true}]",
                    ["actor_id"] = "legacy/default-actor"
                },
                SelectedModel: null),
            new StaticHttpClientFactory(handler),
            [new TikTokApifyScrapeAdapter(), new DefaultApifyScrapeAdapter()]);

        var result = await extractor.ExtractAsync(
            new SocialContentRequest(
                Guid.NewGuid(),
                "https://www.tiktok.com/@brand/video/123",
                "https://www.tiktok.com/@brand/video/123",
                "tiktok",
                "social-extraction"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("clockworks%2Ftiktok-scraper", handler.LastRequestUri?.ToString());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"title\":\"Mapped actor response\"}]")
            });
        }
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StaticHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            _ = name;
            return new HttpClient(_handler, disposeHandler: false);
        }
    }
}
