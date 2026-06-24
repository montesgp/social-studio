using System.Net;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;
using Microsoft.Extensions.Logging.Abstractions;
// WU-9: alias removed — IImageGenerationClient retired; ImageGenerationRequest is unambiguous now.

namespace Incoders.Hive.Tests.Infrastructure.Ai;

/// <summary>
/// WU-6 RED tests: OpenAiImageModelClient implements IImageModelClient.
/// All OpenAI SDK types are confined to the adapter. Returns bytes-only
/// ImageGenerationResult; maps provider errors to Result.Failure; non-null Init
/// must not throw (OQ-7 reserved slot).
/// Refs: IMG-01, IMG-08; design §2.3; tasks 6.1.
/// </summary>
public sealed class OpenAiImageModelClientTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    // OpenAI Images API response shape for b64_json format:
    // { "created": 1234567890, "data": [{ "b64_json": "<base64>", "revised_prompt": "..." }] }
    private static string BuildOpenAiImageResponse(byte[] imageBytes)
    {
        var b64 = Convert.ToBase64String(imageBytes);
        return $$"""
            {
              "created": 1700000000,
              "data": [
                {
                  "b64_json": "{{b64}}",
                  "revised_prompt": "a vivid background"
                }
              ]
            }
            """;
    }

    // -----------------------------------------------------------------------
    // 6.1a: Successful response → Result.Success with correct bytes and mime
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_SuccessResponse_ReturnsBytesWithPngMime()
    {
        var imageBytes = new byte[] { 137, 80, 78, 71, 13, 10 }; // PNG magic bytes
        var response = BuildOpenAiImageResponse(imageBytes);
        var handler = new StaticHandler(HttpStatusCode.OK, response);
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("image/png", result.Value.Mime);
        Assert.Equal(imageBytes, result.Value.Bytes);
        Assert.Equal("openai", result.Value.ProviderKey);
    }

    // -----------------------------------------------------------------------
    // 6.1b: No OpenAI SDK type appears in the returned ImageGenerationResult
    // (bytes-only contract; IMG-08)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_SuccessResponse_ResultContainsOnlyBytes()
    {
        var imageBytes = new byte[] { 1, 2, 3, 4, 5 };
        var response = BuildOpenAiImageResponse(imageBytes);
        var handler = new StaticHandler(HttpStatusCode.OK, response);
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Verify the result type has no provider SDK types by inspecting its assembly.
        var resultType = result.Value.GetType();
        var forbiddenAssemblyPrefixes = new[] { "OpenAI", "Azure.AI", "Anthropic" };
        foreach (var prefix in forbiddenAssemblyPrefixes)
        {
            Assert.DoesNotContain(prefix, resultType.Assembly.FullName ?? string.Empty);
        }
    }

    // -----------------------------------------------------------------------
    // 6.1c: 4xx / 5xx OpenAI error response → Result.Failure (no unhandled exception)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_4xxResponse_ReturnsFailure()
    {
        var handler = new StaticHandler(HttpStatusCode.BadRequest, """{"error":{"message":"invalid request"}}""");
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GenerateAsync_5xxResponse_ReturnsFailure()
    {
        var handler = new StaticHandler(HttpStatusCode.InternalServerError, """{"error":{"message":"server error"}}""");
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GenerateAsync_UnauthorizedResponse_ReturnsFailure()
    {
        var handler = new StaticHandler(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid key"}}""");
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    // -----------------------------------------------------------------------
    // 6.1d: Non-null Init → accepted without error (OQ-7 reserved slot)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_InitIsNonNull_DoesNotThrow()
    {
        var imageBytes = new byte[] { 1, 2, 3 };
        var response = BuildOpenAiImageResponse(imageBytes);
        var handler = new StaticHandler(HttpStatusCode.OK, response);
        var client = BuildClient(handler);

        var request = BuildRequest() with
        {
            Init = new ImageInitReference(Bytes: null, Url: "https://example.com/ref.png", Mime: "image/png")
        };

        // Must not throw; Init is ignored in v1 but must not crash
        var result = await client.GenerateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // -----------------------------------------------------------------------
    // 6.1e: Missing api_key credential → returns Failure before any HTTP call
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_MissingApiKey_ReturnsFailureWithoutHttpCall()
    {
        var handler = new StaticHandler(HttpStatusCode.OK, "{}");
        var credential = new ResolvedProviderCredential(
            Guid.Empty, "openai", IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string>(), // no api_key
            SelectedModel: "dall-e-3");

        var client = new OpenAiImageModelClient(
            credential,
            new StaticHttpClientFactory(handler),
            NullLogger<OpenAiImageModelClient>.Instance);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, handler.CallCount);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ImageGenerationRequest BuildRequest() => new(
        OrganizationId: OrgId,
        CapabilityKey: "ai-image",
        PromptVersionKey: "image-gen/v1",
        Prompt: "a vivid botanical background",
        Width: 1024,
        Height: 1024,
        PreferredProviderKey: "openai",
        PreferredModel: "dall-e-3");

    private static OpenAiImageModelClient BuildClient(HttpMessageHandler handler)
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty, "openai", IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "test-key-123" },
            SelectedModel: "dall-e-3");

        return new OpenAiImageModelClient(
            credential,
            new StaticHttpClientFactory(handler),
            NullLogger<OpenAiImageModelClient>.Instance);
    }
}

// -----------------------------------------------------------------------
// Test doubles
// -----------------------------------------------------------------------

file sealed class StaticHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _body;
    private int _callCount;

    public int CallCount => _callCount;

    public StaticHandler(HttpStatusCode statusCode, string body)
    {
        _statusCode = statusCode;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

file sealed class StaticHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public StaticHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
