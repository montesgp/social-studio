using System.Net;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai.Providers;

namespace SocialStudio.Tests.Infrastructure.Ai;

/// <summary>
/// Phase 2 (CAG2): when AiCompletionRequest.Images are present, the Claude client emits Anthropic
/// vision content blocks (base64 image source + text), and stays a plain string otherwise.
/// </summary>
public sealed class ClaudeModelClientVisionTests
{
    private static ClaudeModelClient Build(HttpMessageHandler handler)
    {
        var credential = new ResolvedProviderCredential(
            Guid.NewGuid(),
            "claude",
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "test-key" },
            "claude-opus-4-8");
        return new ClaudeModelClient(credential, new StubFactory(handler));
    }

    private static AiCompletionRequest Request(IReadOnlyList<AiImageInput>? images) =>
        new(Guid.NewGuid(), "ai-aesthetic", "aesthetic/v1", "Describe this client's aesthetic.", "claude", "claude-opus-4-8", Images: images);

    [Fact]
    public async Task WithImages_EmitsBase64ImageBlockAndText()
    {
        var handler = new CaptureHandler();
        var client = Build(handler);

        await client.CompleteAsync(Request(
        [
            new AiImageInput("image/jpeg", "QkFTRTY0REFUQQ=="),
        ]), CancellationToken.None);

        Assert.NotNull(handler.LastBody);
        Assert.Contains("\"type\":\"image\"", handler.LastBody!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("base64", handler.LastBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("image/jpeg", handler.LastBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QkFTRTY0REFUQQ==", handler.LastBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"type\":\"text\"", handler.LastBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithoutImages_ContentStaysPlainString_NoImageBlock()
    {
        var handler = new CaptureHandler();
        var client = Build(handler);

        await client.CompleteAsync(Request(images: null), CancellationToken.None);

        Assert.NotNull(handler.LastBody);
        Assert.DoesNotContain("\"type\":\"image\"", handler.LastBody!, StringComparison.OrdinalIgnoreCase);
    }
}

file sealed class CaptureHandler : HttpMessageHandler
{
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        // Minimal Claude tool-use success body (the assertions target the REQUEST, not the response).
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"content":[{"type":"tool_use","input":{}}]}"""),
        };
    }
}

file sealed class StubFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public StubFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
