using System.Net;
using System.Text;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

/// <summary>
/// Real-generation bug found on the owner's local ComfyUI (1024 MB VRAM): the queued <c>/prompt</c>
/// is accepted (the graph is valid — the node classes and detection-model weights are genuinely
/// present), but <c>FaceDetailer</c>'s second KSampler pass runs out of GPU memory mid-execution.
/// That failure only ever surfaces via <c>/history</c>'s <c>status_str == "error"</c>, a path the
/// A5/CUI-09-b invalidation logic (<see cref="ComfyUiDetailerInvalidationTests"/>) never covered — it
/// only inspects the <c>/prompt</c> queue-time rejection payload. Left unfixed, every retry and every
/// subsequent slide keeps re-selecting the augmented template against a probe cache that still says
/// <see cref="ComfyUiDetailerSupport.Supported"/>, repeating the same fatal OOM until the 10-minute
/// TTL happens to expire — burning GPU time for a carousel that never produces an image.
/// </summary>
[Collection("ComfyUiDetailerProbeCacheSerial")]
public sealed class ComfyUiDetailerExecutionInvalidationTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly byte[] FixtureImageBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A];

    [Fact]
    public async Task GenerateAsync_DetailerAttributableExecutionFailure_InvalidatesCache_NextJobFallsBackToPlain()
    {
        var baseUrl = UniqueBaseUrl();
        var handler = new ExecutionFailureHandler(
            FixtureImageBytes,
            failFirstExecutionWithDetailerNode: true);
        var client = BuildClient(handler, baseUrl);

        var firstResult = await client.GenerateAsync(BuildRequest(), CancellationToken.None);
        Assert.True(firstResult.IsFailure);
        Assert.Equal(1, handler.ObjectInfoCallCount);
        Assert.Contains("FaceDetailer", handler.LastSubmittedGraph!, StringComparison.Ordinal);

        var secondResult = await client.GenerateAsync(BuildRequest(), CancellationToken.None);
        Assert.True(secondResult.IsSuccess);

        // Invalidation forced a re-probe instead of reusing the still-fresh cached `Supported`
        // entry — a second /object_info call is the only observable proof of that.
        Assert.Equal(2, handler.ObjectInfoCallCount);
        Assert.DoesNotContain("FaceDetailer", handler.LastSubmittedGraph!, StringComparison.Ordinal);
        Assert.DoesNotContain("UltralyticsDetectorProvider", handler.LastSubmittedGraph!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_UnrelatedExecutionFailure_DoesNotInvalidateCache()
    {
        var baseUrl = UniqueBaseUrl();
        var handler = new ExecutionFailureHandler(
            FixtureImageBytes,
            failFirstExecutionWithDetailerNode: false);
        var client = BuildClient(handler, baseUrl);

        var firstResult = await client.GenerateAsync(BuildRequest(), CancellationToken.None);
        Assert.True(firstResult.IsFailure);
        Assert.Equal(1, handler.ObjectInfoCallCount);

        var secondResult = await client.GenerateAsync(BuildRequest(), CancellationToken.None);
        Assert.True(secondResult.IsSuccess);

        // A KSampler OOM unrelated to the detailer subgraph must not thrash the cache.
        Assert.Equal(1, handler.ObjectInfoCallCount);
        Assert.Contains("FaceDetailer", handler.LastSubmittedGraph!, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string UniqueBaseUrl() =>
        $"http://comfyui-exec-invalidation-{Guid.NewGuid():N}.invalid:8188";

    private static ComfyUiImageModelClient BuildClient(HttpMessageHandler handler, string baseUrl)
    {
        var values = new Dictionary<string, string>
        {
            ["base_url"] = baseUrl,
            ["poll_interval_ms"] = "1",
        };

        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            "comfyui",
            IntegrationConnectionMode.SystemDefault,
            values,
            SelectedModel: "sd15-checkpoint.safetensors");

        return new ComfyUiImageModelClient(
            credential,
            new StaticHttpClientFactory(handler),
            new FakeStorage(),
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

    /// <summary>
    /// Scripts <c>/object_info -&gt; /free -&gt; /prompt (always accepted) -&gt; /history</c>, with the
    /// FIRST job's <c>/history</c> poll reporting <c>status_str: "error"</c> attributed either to
    /// <c>FaceDetailer</c> or to an unrelated node, and every subsequent job succeeding.
    /// </summary>
    private sealed class ExecutionFailureHandler : HttpMessageHandler
    {
        private readonly byte[] _fixtureBytes;
        private readonly bool _failFirstExecutionWithDetailerNode;
        private int _historyPollCountForCurrentJob;
        private int _promptCallCount;

        public int ObjectInfoCallCount { get; private set; }
        public string? LastSubmittedGraph { get; private set; }

        public ExecutionFailureHandler(byte[] fixtureBytes, bool failFirstExecutionWithDetailerNode)
        {
            _fixtureBytes = fixtureBytes;
            _failFirstExecutionWithDetailerNode = failFirstExecutionWithDetailerNode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.Contains("/object_info", StringComparison.OrdinalIgnoreCase))
            {
                ObjectInfoCallCount++;

                // The second probe simulates a different observed backend state so the assertions
                // can distinguish "invalidation forced a re-probe" from "the still-cached Supported
                // entry was reused" purely from which template the next job submits.
                var body = ObjectInfoCallCount == 1
                    ? """{"FaceDetailer":{},"UltralyticsDetectorProvider":{}}"""
                    : """{"KSampler":{}}""";
                return JsonResponse(body);
            }

            if (path.EndsWith("/free", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("{}");
            }

            if (path.Contains("/upload/image", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{"name":"uploaded_ref.png","subfolder":"","type":"input"}""");
            }

            // NOTE: check "/history/" before "/prompt" — the prompt id happens to start with
            // "prompt" so "/history/prompt-123" contains "/prompt" as a substring.
            if (path.Contains("/history/", StringComparison.OrdinalIgnoreCase))
            {
                _historyPollCountForCurrentJob++;

                if (_promptCallCount == 1 && _historyPollCountForCurrentJob == 1)
                {
                    var nodeType = _failFirstExecutionWithDetailerNode ? "FaceDetailer" : "KSampler";
                    var body = "{\"prompt-123\":{\"status\":{\"status_str\":\"error\",\"messages\":" +
                        "[[\"execution_error\",{\"node_type\":\"" + nodeType + "\",\"exception_type\":\"RuntimeError\"," +
                        "\"exception_message\":\"Could not allocate tensor: not enough GPU video memory available!\"}]]}}}";
                    return JsonResponse(body);
                }

                _historyPollCountForCurrentJob = 0;
                return JsonResponse(
                    """{"prompt-123":{"outputs":{"9":{"images":[{"filename":"out.png","subfolder":"","type":"output"}]}}}}""");
            }

            if (path.Contains("/prompt", StringComparison.OrdinalIgnoreCase))
            {
                _promptCallCount++;
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                LastSubmittedGraph = doc.RootElement.GetProperty("prompt").GetRawText();

                return JsonResponse("""{"prompt_id":"prompt-123"}""");
            }

            if (path.Contains("/view", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_fixtureBytes),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StaticHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeStorage : IGeneratedAssetStorage
    {
        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct) =>
            Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
            => Task.FromResult(new StoredAssetRef("path", content.Mime, content.Bytes.Length, "hash"));
        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
        public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
    }
}
