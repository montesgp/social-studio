using System.Net;
using System.Text;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Infrastructure.Ai;

/// <summary>
/// WU-3 RED tests (task 3.1): every ComfyUI failure/timeout path maps onto the exact
/// <c>IntegrationErrors</c> code <see cref="OpenAiImageModelClient"/> already uses, and no path
/// propagates an unhandled exception. Refs: CUI-02-a, CUI-02-b, CUI-02-c, CUI-06-b; design §3.4
/// error table, §5.
/// </summary>
public sealed class ComfyUiErrorMappingTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public async Task GenerateAsync_ServerUnreachable_ReturnsExternalProviderUnavailable()
    {
        var handler = new ThrowingHandler(new HttpRequestException("Connection refused"));
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ExternalProviderUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task GenerateAsync_PromptEndpointReturns500_ReturnsExternalProviderUnavailable()
    {
        var handler = new RoutedHandler(promptStatusCode: HttpStatusCode.InternalServerError);
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ExternalProviderUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task GenerateAsync_PromptResponseHasNodeErrors_ReturnsExternalProviderUnavailable()
    {
        var handler = new RoutedHandler(
            promptBody: """{"prompt_id":"prompt-123","node_errors":{"8":{"errors":[{"message":"bad input"}]}}}""");
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ExternalProviderUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task GenerateAsync_HistoryStatusError_ReturnsExternalProviderUnavailable()
    {
        var handler = new RoutedHandler(
            historyBody: """{"prompt-123":{"status":{"status_str":"error"},"outputs":{}}}""");
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ExternalProviderUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task GenerateAsync_PollDeadlineElapses_ReturnsProviderTimeout()
    {
        // History never carries the completed outputs -> the poll loop must be bounded by
        // ComfyUiSettings.Timeout and return ProviderTimeout instead of looping forever.
        var handler = new RoutedHandler(historyBody: "{}");
        var client = BuildClient(handler, timeoutMs: "20", pollIntervalMs: "5");

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderTimeout.Code, result.Error.Code);
    }

    [Fact]
    public async Task GenerateAsync_CallerTokenCancelled_ReturnsOperationCancelledNotProviderTimeout()
    {
        // These two used to collapse onto ProviderTimeout, and the difference matters: the provider
        // running out of ITS budget is a retryable provider fault, while the CALLER withdrawing the
        // work is not. Reported as a timeout, a cancelled job's remaining slides each bought two
        // more full provider attempts.
        var handler = new RoutedHandler(historyBody: "{}");
        var client = BuildClient(handler, pollIntervalMs: "5");
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(20);

        var result = await client.GenerateAsync(BuildRequest(), cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.OperationCancelled.Code, result.Error.Code);
    }

    [Fact]
    public async Task GenerateAsync_ViewReturnsEmptyBody_ReturnsAiJsonInvalid()
    {
        var handler = new RoutedHandler(viewBody: []);
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.AiJsonInvalid.Code, result.Error.Code);
    }

    [Fact]
    public async Task GenerateAsync_InvalidSettings_ReturnsProviderCredentialUnavailable()
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            "comfyui",
            IntegrationConnectionMode.SystemDefault,
            new Dictionary<string, string> { ["base_url"] = "not-a-valid-url" },
            SelectedModel: "sd15.safetensors");

        var client = new ComfyUiImageModelClient(
            credential,
            new StaticHttpClientFactory(new RoutedHandler()),
            new EmptyStorage(),
            NullLogger<ComfyUiImageModelClient>.Instance);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderCredentialUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public async Task GenerateAsync_NoCheckpointOverrideAvailable_ReturnsAiModelRequired()
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            "comfyui",
            IntegrationConnectionMode.SystemDefault,
            new Dictionary<string, string> { ["base_url"] = "http://127.0.0.1:8188" },
            SelectedModel: null);

        var client = new ComfyUiImageModelClient(
            credential,
            new StaticHttpClientFactory(new RoutedHandler()),
            new EmptyStorage(),
            NullLogger<ComfyUiImageModelClient>.Instance);

        var result = await client.GenerateAsync(BuildRequest() with { PreferredModel = null }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.AiModelRequired.Code, result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ComfyUiImageModelClient BuildClient(
        HttpMessageHandler handler,
        string timeoutMs = "180000",
        string pollIntervalMs = "1")
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            "comfyui",
            IntegrationConnectionMode.SystemDefault,
            new Dictionary<string, string>
            {
                ["base_url"] = "http://127.0.0.1:8188",
                ["timeout_ms"] = timeoutMs,
                ["poll_interval_ms"] = pollIntervalMs,
            },
            SelectedModel: "sd15-checkpoint.safetensors");

        return new ComfyUiImageModelClient(
            credential,
            new StaticHttpClientFactory(handler),
            new EmptyStorage(),
            NullLogger<ComfyUiImageModelClient>.Instance);
    }

    private static ImageGenerationRequest BuildRequest() => new(
        OrganizationId: OrgId,
        CapabilityKey: "ai-image",
        PromptVersionKey: "image-gen/v1",
        Prompt: "a vivid botanical background",
        Width: 512,
        Height: 512,
        PreferredProviderKey: "comfyui",
        PreferredModel: null);

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;
        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => throw _exception;
    }

    /// <summary>Scripts a full happy path with individually overridable failure points.</summary>
    private sealed class RoutedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _promptStatusCode;
        private readonly string _promptBody;
        private readonly string? _historyBody;
        private readonly byte[]? _viewBody;

        public RoutedHandler(
            HttpStatusCode promptStatusCode = HttpStatusCode.OK,
            string promptBody = """{"prompt_id":"prompt-123"}""",
            string? historyBody = null,
            byte[]? viewBody = null)
        {
            _promptStatusCode = promptStatusCode;
            _promptBody = promptBody;
            _historyBody = historyBody;
            _viewBody = viewBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.Contains("/upload/image", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("""{"name":"uploaded_ref.png","subfolder":"","type":"input"}"""));
            }

            if (path.Contains("/history/", StringComparison.OrdinalIgnoreCase))
            {
                var body = _historyBody ??
                    """{"prompt-123":{"outputs":{"9":{"images":[{"filename":"out.png","subfolder":"","type":"output"}]}}}}""";
                return Task.FromResult(JsonResponse(body));
            }

            if (path.Contains("/prompt", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(_promptStatusCode)
                {
                    Content = new StringContent(_promptBody, Encoding.UTF8, "application/json"),
                });
            }

            if (path.Contains("/view", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = _viewBody ?? [0x89, 0x50, 0x4E, 0x47];
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    // A workflow that fails to EXECUTE and a provider that is DOWN both surface as
    // ExternalProviderUnavailable, so the only way to tell them apart is the logged detail. When a
    // resolution change made every KSampler run out of memory, the logs read as an outage and the
    // real cause stayed invisible for a full diagnostic cycle. These tests pin that detail.
    [Fact]
    public void DescribeExecutionError_SurfacesFailingNodeAndBackendException()
    {
        var status = System.Text.Json.JsonDocument.Parse(
            """
            {
              "status_str": "error",
              "messages": [
                ["execution_start", {}],
                ["execution_cached", {}],
                ["execution_error", {
                  "node_type": "KSampler",
                  "exception_type": "RuntimeError",
                  "exception_message": "Could not allocate tensor with 4033420304 bytes. There is not enough GPU video memory available!"
                }]
              ]
            }
            """).RootElement;

        var description = ComfyUiImageModelClient.DescribeExecutionError(status);

        Assert.Contains("KSampler", description, StringComparison.Ordinal);
        Assert.Contains("RuntimeError", description, StringComparison.Ordinal);
        Assert.Contains("not enough GPU video memory", description, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeExecutionError_MissingOrMalformedDetail_DoesNotThrow()
    {
        var noMessages = System.Text.Json.JsonDocument.Parse("""{"status_str":"error"}""").RootElement;
        Assert.False(string.IsNullOrWhiteSpace(ComfyUiImageModelClient.DescribeExecutionError(noMessages)));

        var wrongShape = System.Text.Json.JsonDocument.Parse(
            """{"status_str":"error","messages":[["execution_error",{}]]}""").RootElement;
        var description = ComfyUiImageModelClient.DescribeExecutionError(wrongShape);
        Assert.Contains("unknown", description, StringComparison.Ordinal);
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StaticHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class EmptyStorage : IGeneratedAssetStorage
    {
        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
            => Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
            => Task.FromResult(new StoredAssetRef("path", content.Mime, content.Bytes.Length, "hash"));
        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
        public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
    }
}
