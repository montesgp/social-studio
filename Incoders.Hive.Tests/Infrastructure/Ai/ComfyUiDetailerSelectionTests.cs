using System.Net;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Ai;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

/// <summary>
/// WU-3 RED tests (tasks 3.1-3.4): <c>ComfyUiImageModelClient.GenerateAsync</c> selects the
/// detailer-augmented template only on a <c>Supported</c> probe with the opt-out flag unset; every
/// other outcome — <c>NotSupported</c>, <c>Unknown</c>, or opt-out — selects the plain template, and
/// opt-out skips the probe entirely (zero <c>/object_info</c> calls). Refs: A1, A6; CUI-08.
/// </summary>
[Collection("ComfyUiDetailerProbeCacheSerial")]
public sealed class ComfyUiDetailerSelectionTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly byte[] FixtureImageBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A];

    // -----------------------------------------------------------------------
    // 3.1 — one assertion per selection-table row
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_SupportedProbe_DetailerEnabled_SelectsAugmentedTemplate()
    {
        var baseUrl = UniqueBaseUrl();
        var handler = new SelectionHandler(FixtureImageBytes, detailerSupported: true);
        var client = BuildClient(handler, baseUrl, detailerEnabled: null);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(handler.ObjectInfoCallCount >= 1);
        Assert.Contains("FaceDetailer", handler.SubmittedGraph, StringComparison.Ordinal);
        Assert.Contains("UltralyticsDetectorProvider", handler.SubmittedGraph, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_NotSupportedProbe_SelectsPlainTemplate()
    {
        var baseUrl = UniqueBaseUrl();
        var handler = new SelectionHandler(FixtureImageBytes, detailerSupported: false);
        var client = BuildClient(handler, baseUrl, detailerEnabled: null);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(handler.ObjectInfoCallCount >= 1);
        Assert.DoesNotContain("FaceDetailer", handler.SubmittedGraph, StringComparison.Ordinal);
        Assert.DoesNotContain("UltralyticsDetectorProvider", handler.SubmittedGraph, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_UnknownProbe_TransportFailure_SelectsPlainTemplate()
    {
        var baseUrl = UniqueBaseUrl();
        var handler = new SelectionHandler(FixtureImageBytes, detailerSupported: false) { ObjectInfoThrows = true };
        var client = BuildClient(handler, baseUrl, detailerEnabled: null);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("FaceDetailer", handler.SubmittedGraph, StringComparison.Ordinal);
        Assert.DoesNotContain("UltralyticsDetectorProvider", handler.SubmittedGraph, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_SupportedProbe_DetailerDisabled_SelectsPlainAndCallsNoObjectInfo()
    {
        var baseUrl = UniqueBaseUrl();
        var handler = new SelectionHandler(FixtureImageBytes, detailerSupported: true);
        var client = BuildClient(handler, baseUrl, detailerEnabled: false);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, handler.ObjectInfoCallCount);
        Assert.DoesNotContain("FaceDetailer", handler.SubmittedGraph, StringComparison.Ordinal);
        Assert.DoesNotContain("UltralyticsDetectorProvider", handler.SubmittedGraph, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // 3.2 — graph wiring on a Supported probe
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_SupportedProbe_GraphWiresDetailerBetweenSamplerAndOutput()
    {
        var baseUrl = UniqueBaseUrl();
        var handler = new SelectionHandler(FixtureImageBytes, detailerSupported: true);
        var client = BuildClient(handler, baseUrl, detailerEnabled: null);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(handler.SubmittedGraph!);
        var saveImageInputs = doc.RootElement.GetProperty("9").GetProperty("inputs");
        var imagesEdge = saveImageInputs.GetProperty("images");
        Assert.Equal("11", imagesEdge[0].GetString());
        Assert.Equal(
            "FaceDetailer",
            doc.RootElement.GetProperty("11").GetProperty("class_type").GetString());
        Assert.Equal(
            "UltralyticsDetectorProvider",
            doc.RootElement.GetProperty("10").GetProperty("class_type").GetString());
    }

    // -----------------------------------------------------------------------
    // 3.3 — byte-identical plain graph on NotSupported/Unknown (regression)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_NotSupportedProbe_GraphIsByteIdenticalToPlainTemplateSubmission()
    {
        var plainOnlyBaseUrl = UniqueBaseUrl();
        var plainOnlyHandler = new SelectionHandler(FixtureImageBytes, detailerSupported: false)
        {
            // No /object_info handler at all — mirrors pre-change behaviour where the probe
            // never existed; ProbeAsync must resolve this to Unknown without throwing.
            ObjectInfoReturns404 = true,
        };
        var plainClient = BuildClient(plainOnlyHandler, plainOnlyBaseUrl, detailerEnabled: false);
        var plainResult = await plainClient.GenerateAsync(BuildRequest(), CancellationToken.None);

        var notSupportedBaseUrl = UniqueBaseUrl();
        var notSupportedHandler = new SelectionHandler(FixtureImageBytes, detailerSupported: false);
        var notSupportedClient = BuildClient(notSupportedHandler, notSupportedBaseUrl, detailerEnabled: null);
        var notSupportedResult = await notSupportedClient.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(plainResult.IsSuccess);
        Assert.True(notSupportedResult.IsSuccess);

        // Normalize the varying seed token before comparing, since GenerateAsync draws a fresh
        // random seed per call — everything else in the submitted graph must be byte-identical.
        var normalizedPlain = NormalizeSeed(plainOnlyHandler.SubmittedGraph!);
        var normalizedNotSupported = NormalizeSeed(notSupportedHandler.SubmittedGraph!);
        Assert.Equal(normalizedPlain, normalizedNotSupported);
    }

    private static string NormalizeSeed(string graphJson)
    {
        using var doc = JsonDocument.Parse(graphJson);
        var seed = doc.RootElement.GetProperty("8").GetProperty("inputs").GetProperty("seed").GetInt64();
        return graphJson.Replace(seed.ToString(), "SEED", StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string UniqueBaseUrl() =>
        $"http://comfyui-selection-{Guid.NewGuid():N}.invalid:8188";

    private static ComfyUiImageModelClient BuildClient(
        HttpMessageHandler handler,
        string baseUrl,
        bool? detailerEnabled)
    {
        var values = new Dictionary<string, string>
        {
            ["base_url"] = baseUrl,
            ["poll_interval_ms"] = "1",
        };

        if (detailerEnabled is { } flag)
        {
            values["detailer_enabled"] = flag ? "true" : "false";
        }

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
    /// Scripts <c>/object_info -&gt; /free -&gt; /prompt -&gt; poll /history -&gt; /view</c>, capturing
    /// the exact graph body posted to <c>/prompt</c> for assertion.
    /// </summary>
    private sealed class SelectionHandler : HttpMessageHandler
    {
        private readonly byte[] _fixtureBytes;
        private readonly bool _detailerSupported;
        private int _historyCallCount;

        public int ObjectInfoCallCount { get; private set; }
        public string? SubmittedGraph { get; private set; }
        public bool ObjectInfoThrows { get; init; }
        public bool ObjectInfoReturns404 { get; init; }

        public SelectionHandler(byte[] fixtureBytes, bool detailerSupported)
        {
            _fixtureBytes = fixtureBytes;
            _detailerSupported = detailerSupported;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.Contains("/object_info", StringComparison.OrdinalIgnoreCase))
            {
                ObjectInfoCallCount++;

                if (ObjectInfoThrows)
                {
                    throw new HttpRequestException("simulated transport failure");
                }

                if (ObjectInfoReturns404)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                var body = _detailerSupported
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
                if (_historyCallCount < 2)
                {
                    return JsonResponse("{}");
                }

                return JsonResponse(
                    """{"prompt-123":{"outputs":{"9":{"images":[{"filename":"out.png","subfolder":"","type":"output"}]}}}}""");
            }

            if (path.Contains("/prompt", StringComparison.OrdinalIgnoreCase))
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(body);
                SubmittedGraph = doc.RootElement.GetProperty("prompt").GetRawText();
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
