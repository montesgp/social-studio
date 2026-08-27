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
/// WU-4 RED tests (tasks 4.1-4.2): <c>ComfyUiImageModelClient.QueuePromptAsync</c> invalidates the
/// cached <see cref="ComfyUiDetailerSupport.Supported"/> probe entry ONLY when the augmented
/// template was submitted AND the <c>/prompt</c> rejection is detailer-attributable (A5, CUI-09-b).
/// Any other kind of failure — unrelated node error, checkpoint missing, generic outage — must NOT
/// evict the cache.
/// </summary>
[Collection("ComfyUiDetailerProbeCacheSerial")]
public sealed class ComfyUiDetailerInvalidationTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly byte[] FixtureImageBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A];

    // -----------------------------------------------------------------------
    // 4.1 — detailer-attributable rejection invalidates the cache; the NEXT
    // generation re-probes instead of riding out the TTL on the stale entry.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QueuePromptAsync_DetailerAttributableRejection_InvalidatesCache_NextJobRePlobesAndFallsBackToPlain()
    {
        var baseUrl = UniqueBaseUrl();
        var handler = new InvalidationHandler(
            FixtureImageBytes,
            detailerSupportedOnFirstProbe: true,
            detailerSupportedOnSubsequentProbes: false,
            rejectFirstPromptWithDetailerNodeErrors: true);
        var client = BuildClient(handler, baseUrl);

        var firstResult = await client.GenerateAsync(BuildRequest(), CancellationToken.None);
        Assert.True(firstResult.IsFailure);
        Assert.Equal(1, handler.ObjectInfoCallCount);
        Assert.NotNull(handler.LastSubmittedGraph);
        Assert.Contains("FaceDetailer", handler.LastSubmittedGraph!, StringComparison.Ordinal);

        var secondResult = await client.GenerateAsync(BuildRequest(), CancellationToken.None);
        Assert.True(secondResult.IsSuccess);

        // Invalidation forced a re-probe instead of reusing the still-fresh cached `Supported`
        // entry — a second /object_info call is the only observable proof of that.
        Assert.Equal(2, handler.ObjectInfoCallCount);
        Assert.DoesNotContain("FaceDetailer", handler.LastSubmittedGraph!, StringComparison.Ordinal);
        Assert.DoesNotContain("UltralyticsDetectorProvider", handler.LastSubmittedGraph!, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // 4.2 — an unrelated failure must NOT invalidate the cache.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QueuePromptAsync_UnrelatedRejection_DoesNotInvalidateCache_NextJobReusesCachedSupported()
    {
        var baseUrl = UniqueBaseUrl();
        var handler = new InvalidationHandler(
            FixtureImageBytes,
            detailerSupportedOnFirstProbe: true,
            detailerSupportedOnSubsequentProbes: true,
            rejectFirstPromptWithDetailerNodeErrors: false,
            rejectFirstPromptWithUnrelatedNodeErrors: true);
        var client = BuildClient(handler, baseUrl);

        var firstResult = await client.GenerateAsync(BuildRequest(), CancellationToken.None);
        Assert.True(firstResult.IsFailure);
        Assert.Equal(1, handler.ObjectInfoCallCount);

        var secondResult = await client.GenerateAsync(BuildRequest(), CancellationToken.None);
        Assert.True(secondResult.IsSuccess);

        // No invalidation happened: the cached `Supported` entry (still well within its 10-minute
        // TTL) is reused, so the second job must NOT re-probe.
        Assert.Equal(1, handler.ObjectInfoCallCount);
        Assert.Contains("FaceDetailer", handler.LastSubmittedGraph!, StringComparison.Ordinal);
        Assert.Contains("UltralyticsDetectorProvider", handler.LastSubmittedGraph!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueuePromptAsync_GenericProviderOutage_DoesNotInvalidateCache()
    {
        var baseUrl = UniqueBaseUrl();
        var handler = new InvalidationHandler(
            FixtureImageBytes,
            detailerSupportedOnFirstProbe: true,
            detailerSupportedOnSubsequentProbes: true,
            rejectFirstPromptWith500: true);
        var client = BuildClient(handler, baseUrl);

        var firstResult = await client.GenerateAsync(BuildRequest(), CancellationToken.None);
        Assert.True(firstResult.IsFailure);
        Assert.Equal(1, handler.ObjectInfoCallCount);

        var secondResult = await client.GenerateAsync(BuildRequest(), CancellationToken.None);
        Assert.True(secondResult.IsSuccess);
        Assert.Equal(1, handler.ObjectInfoCallCount);
        Assert.Contains("FaceDetailer", handler.LastSubmittedGraph!, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string UniqueBaseUrl() =>
        $"http://comfyui-invalidation-{Guid.NewGuid():N}.invalid:8188";

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
    /// Scripts <c>/object_info -&gt; /free -&gt; /prompt -&gt; poll /history -&gt; /view</c> per call,
    /// with the FIRST <c>/prompt</c> call scriptable to reject with detailer-attributable,
    /// unrelated, or generic-outage errors, and every subsequent call succeeding.
    /// </summary>
    private sealed class InvalidationHandler : HttpMessageHandler
    {
        private readonly byte[] _fixtureBytes;
        private readonly bool _detailerSupportedOnFirstProbe;
        private readonly bool _detailerSupportedOnSubsequentProbes;
        private readonly bool _rejectFirstPromptWithDetailerNodeErrors;
        private readonly bool _rejectFirstPromptWithUnrelatedNodeErrors;
        private readonly bool _rejectFirstPromptWith500;
        private int _historyCallCount;
        private int _promptCallCount;

        public int ObjectInfoCallCount { get; private set; }
        public string? LastSubmittedGraph { get; private set; }

        public InvalidationHandler(
            byte[] fixtureBytes,
            bool detailerSupportedOnFirstProbe,
            bool detailerSupportedOnSubsequentProbes,
            bool rejectFirstPromptWithDetailerNodeErrors = false,
            bool rejectFirstPromptWithUnrelatedNodeErrors = false,
            bool rejectFirstPromptWith500 = false)
        {
            _fixtureBytes = fixtureBytes;
            _detailerSupportedOnFirstProbe = detailerSupportedOnFirstProbe;
            _detailerSupportedOnSubsequentProbes = detailerSupportedOnSubsequentProbes;
            _rejectFirstPromptWithDetailerNodeErrors = rejectFirstPromptWithDetailerNodeErrors;
            _rejectFirstPromptWithUnrelatedNodeErrors = rejectFirstPromptWithUnrelatedNodeErrors;
            _rejectFirstPromptWith500 = rejectFirstPromptWith500;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.Contains("/object_info", StringComparison.OrdinalIgnoreCase))
            {
                ObjectInfoCallCount++;

                var supported = ObjectInfoCallCount == 1
                    ? _detailerSupportedOnFirstProbe
                    : _detailerSupportedOnSubsequentProbes;

                var body = supported
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
                _historyCallCount++;
                if (_historyCallCount % 2 == 1)
                {
                    return JsonResponse("{}");
                }

                return JsonResponse(
                    """{"prompt-123":{"outputs":{"9":{"images":[{"filename":"out.png","subfolder":"","type":"output"}]}}}}""");
            }

            if (path.Contains("/prompt", StringComparison.OrdinalIgnoreCase))
            {
                _promptCallCount++;
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                LastSubmittedGraph = doc.RootElement.GetProperty("prompt").GetRawText();

                if (_promptCallCount == 1)
                {
                    if (_rejectFirstPromptWithDetailerNodeErrors)
                    {
                        return JsonResponse(
                            """{"node_errors":{"11":{"errors":[{"message":"FaceDetailer: bbox_detector missing"}]}}}""");
                    }

                    if (_rejectFirstPromptWithUnrelatedNodeErrors)
                    {
                        return JsonResponse(
                            """{"node_errors":{"6":{"errors":[{"message":"ckpt_name not found"}]}}}""");
                    }

                    if (_rejectFirstPromptWith500)
                    {
                        return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                        {
                            Content = new StringContent("internal error", Encoding.UTF8, "text/plain"),
                        };
                    }
                }

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
