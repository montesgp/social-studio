using System.Net;
using System.Text;
using SocialStudio.Application.Abstractions.Social.Publications;
using SocialStudio.Infrastructure.Social.Publications;

namespace SocialStudio.Tests.Infrastructure.Social.Publications;

/// <summary>
/// WU2 (Phase 6) TDD tests for <see cref="InstagramGraphPublicationConnector"/>. Fully offline — a
/// stubbed <see cref="HttpMessageHandler"/> stands in for the Graph API; no live network calls.
/// </summary>
public sealed class InstagramGraphPublicationConnectorTests
{
    private static readonly ResolvedPublicationRequest Request = new(
        OrganizationId: Guid.NewGuid(),
        ClientId: Guid.NewGuid(),
        ContentItemId: Guid.NewGuid(),
        ExternalAccountId: "17841400000000000",
        AccessToken: "plaintext-token",
        NetworkKey: "instagram",
        Caption: "Hello world",
        Assets: [new PublicationAssetRef("https://cdn.example.com/asset.png", "image/png")]);

    [Fact]
    public async Task PublishAsync_ContainerCreateAndPublishSucceed_ReturnsExternalReference()
    {
        var handler = new SequencedGraphHandler(
            containerResponse: (HttpStatusCode.OK, "{\"id\":\"container-123\"}"),
            publishResponse: (HttpStatusCode.OK, "{\"id\":\"media-999\"}"));
        var connector = new InstagramGraphPublicationConnector(new HttpClient(handler));

        var result = await connector.PublishAsync(Request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("media-999", result.Value.ExternalReference);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task PublishAsync_ContainerCreateFails_ReturnsFailureWithoutCallingPublish()
    {
        var handler = new SequencedGraphHandler(
            containerResponse: (HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Invalid image URL\",\"type\":\"OAuthException\",\"code\":100}}"),
            publishResponse: (HttpStatusCode.OK, "{\"id\":\"media-999\"}"));
        var connector = new InstagramGraphPublicationConnector(new HttpClient(handler));

        var result = await connector.PublishAsync(Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task PublishAsync_PublishStepFails_ReturnsFailureNoException()
    {
        var handler = new SequencedGraphHandler(
            containerResponse: (HttpStatusCode.OK, "{\"id\":\"container-123\"}"),
            publishResponse: (HttpStatusCode.InternalServerError, "{\"error\":{\"message\":\"Publish failed\",\"type\":\"OAuthException\",\"code\":1}}"));
        var connector = new InstagramGraphPublicationConnector(new HttpClient(handler));

        var result = await connector.PublishAsync(Request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task PublishAsync_NoAssets_ReturnsFailureWithoutAnyHttpCall()
    {
        var handler = new SequencedGraphHandler(
            containerResponse: (HttpStatusCode.OK, "{\"id\":\"container-123\"}"),
            publishResponse: (HttpStatusCode.OK, "{\"id\":\"media-999\"}"));
        var connector = new InstagramGraphPublicationConnector(new HttpClient(handler));

        var request = Request with { Assets = [] };
        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, handler.CallCount);
    }

    // ─── Carousel (WU3 — Assets.Count > 1) ─────────────────────────────────────

    [Fact]
    public async Task PublishAsync_MultipleAssets_CreatesOneChildContainerPerAssetThenCarouselThenPublishes()
    {
        var handler = new CarouselGraphHandler(childCount: 3, carouselContainerId: "carousel-container-1", mediaId: "media-carousel-1");
        var connector = new InstagramGraphPublicationConnector(new HttpClient(handler));

        var request = Request with
        {
            Assets =
            [
                new PublicationAssetRef("https://cdn.example.com/1.png", "image/png"),
                new PublicationAssetRef("https://cdn.example.com/2.png", "image/png"),
                new PublicationAssetRef("https://cdn.example.com/3.png", "image/png"),
            ],
        };

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("media-carousel-1", result.Value.ExternalReference);
        // 3 child containers + 1 carousel container + 1 publish call.
        Assert.Equal(5, handler.CallCount);
        Assert.Equal(3, handler.ChildRequestCount);
        Assert.NotNull(handler.CarouselRequestForm);
        Assert.Equal("CAROUSEL", handler.CarouselRequestForm!["media_type"]);
        Assert.Equal(3, handler.CarouselRequestForm["children"].Split(',').Length);
        Assert.True(handler.CarouselRequestForm.ContainsKey("caption"));
        Assert.True(handler.ChildRequestForms.All(f => f["is_carousel_item"] == "true"));
        Assert.True(handler.ChildRequestForms.All(f => !f.ContainsKey("caption")));
    }

    [Fact]
    public async Task PublishAsync_MultipleAssets_ChildContainerFails_ReturnsFailureWithoutCarouselOrPublishCall()
    {
        var handler = new CarouselGraphHandler(
            childCount: 3, carouselContainerId: "carousel-container-1", mediaId: "media-carousel-1",
            failChildAtIndex: 2);
        var connector = new InstagramGraphPublicationConnector(new HttpClient(handler));

        var request = Request with
        {
            Assets =
            [
                new PublicationAssetRef("https://cdn.example.com/1.png", "image/png"),
                new PublicationAssetRef("https://cdn.example.com/2.png", "image/png"),
                new PublicationAssetRef("https://cdn.example.com/3.png", "image/png"),
            ],
        };

        var result = await connector.PublishAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        // First child succeeds, second child fails — never reaches a third child, the carousel
        // container call, or the publish call.
        Assert.Equal(2, handler.CallCount);
    }

    /// <summary>Routes by path suffix: "/media" (container create) vs "/media_publish" (publish).</summary>
    private sealed class SequencedGraphHandler : HttpMessageHandler
    {
        private readonly (HttpStatusCode Status, string Body) _containerResponse;
        private readonly (HttpStatusCode Status, string Body) _publishResponse;

        public int CallCount { get; private set; }

        public SequencedGraphHandler(
            (HttpStatusCode, string) containerResponse, (HttpStatusCode, string) publishResponse)
        {
            _containerResponse = containerResponse;
            _publishResponse = publishResponse;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var path = request.RequestUri!.AbsolutePath;
            var (status, body) = path.EndsWith("/media_publish", StringComparison.Ordinal)
                ? _publishResponse
                : _containerResponse;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// Distinguishes child-container calls (form carries <c>is_carousel_item=true</c>) from the parent
    /// carousel container call (form carries <c>media_type=CAROUSEL</c>) from the publish call (path
    /// ends with <c>/media_publish</c>). Optionally fails a specific 1-based child call.
    /// </summary>
    private sealed class CarouselGraphHandler : HttpMessageHandler
    {
        private readonly int _childCount;
        private readonly string _carouselContainerId;
        private readonly string _mediaId;
        private readonly int? _failChildAtIndex;
        private int _childCalls;

        public int CallCount { get; private set; }
        public int ChildRequestCount { get; private set; }
        public List<Dictionary<string, string>> ChildRequestForms { get; } = [];
        public Dictionary<string, string>? CarouselRequestForm { get; private set; }

        public CarouselGraphHandler(
            int childCount, string carouselContainerId, string mediaId, int? failChildAtIndex = null)
        {
            _childCount = childCount;
            _carouselContainerId = carouselContainerId;
            _mediaId = mediaId;
            _failChildAtIndex = failChildAtIndex;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/media_publish", StringComparison.Ordinal))
            {
                return Ok($"{{\"id\":\"{_mediaId}\"}}");
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var form = ParseFormUrlEncoded(body);

            if (form.ContainsKey("is_carousel_item"))
            {
                _childCalls++;
                ChildRequestCount++;
                ChildRequestForms.Add(form);

                if (_failChildAtIndex == _childCalls)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(
                            "{\"error\":{\"message\":\"Invalid image URL\",\"type\":\"OAuthException\",\"code\":100}}",
                            Encoding.UTF8, "application/json"),
                    };
                }

                return Ok($"{{\"id\":\"child-container-{_childCalls}\"}}");
            }

            // Parent carousel container call.
            CarouselRequestForm = form;
            return Ok($"{{\"id\":\"{_carouselContainerId}\"}}");
        }

        private static HttpResponseMessage Ok(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static Dictionary<string, string> ParseFormUrlEncoded(string body) =>
            body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(
                    parts => Uri.UnescapeDataString(parts[0]),
                    parts => Uri.UnescapeDataString(parts.Length > 1 ? parts[1] : string.Empty));
    }
}
