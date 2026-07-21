using System.Net;
using System.Text;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Infrastructure.Social.Publications;

namespace Incoders.Hive.Tests.Infrastructure.Social.Publications;

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
}
