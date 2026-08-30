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
/// WU-2 RED tests (task 2.3): <c>ComfyUiImageModelClient.GenerateAsync</c> happy path — a fake
/// <see cref="HttpMessageHandler"/> scripts <c>upload -&gt; /prompt -&gt; poll /history -&gt; /view</c>.
/// Refs: CUI-01-a, CUI-01-b, CUI-01-c, CUI-IMG-01; design §3.2, §3.3, §3.4.
/// </summary>
public sealed class ComfyUiHappyPathTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly byte[] FixtureImageBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A];

    [Fact]
    public async Task GenerateAsync_ConditionedRequest_ReturnsFixtureBytesWithResolvedCheckpoint()
    {
        var storage = new FakeStorage(new Dictionary<string, byte[]> { ["org/ref1.jpg"] = [1, 2, 3] });
        var handler = new ScriptedHandler(FixtureImageBytes);
        var client = BuildClient(handler, storage);

        var request = BuildRequest() with
        {
            ReferenceImages = new List<ImageInitReference>
            {
                new(Bytes: null, Url: "org/ref1.jpg", Mime: "image/jpeg"),
            },
        };

        var result = await client.GenerateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FixtureImageBytes, result.Value.Bytes);
        Assert.Equal("comfyui", result.Value.ProviderKey);
        Assert.Equal("sd15-checkpoint.safetensors", result.Value.Model);
        Assert.True(handler.UploadCalled);
    }

    // A carousel generates its slides back to back against one ComfyUI process, which does not
    // reclaim cached VRAM between runs. Measured on the reference backend: the first image succeeds
    // and the next fails allocating as little as 209 MB, then the same request succeeds once the
    // cache is dropped. That is the intermittency behind "sometimes it generates, sometimes not".
    [Fact]
    public async Task GenerateAsync_ReclaimsVideoMemoryBeforeQueueingTheWorkflow()
    {
        var handler = new ScriptedHandler(FixtureImageBytes);
        var client = BuildClient(handler, new FakeStorage(new Dictionary<string, byte[]>()));

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var freeIndex = handler.CallOrder.FindIndex(p => p.EndsWith("/free", StringComparison.OrdinalIgnoreCase));
        var promptIndex = handler.CallOrder.FindIndex(p => p.EndsWith("/prompt", StringComparison.OrdinalIgnoreCase));

        Assert.True(freeIndex >= 0, "Expected a /free call before queueing the workflow.");
        Assert.True(promptIndex >= 0, "Expected the workflow to be queued.");
        Assert.True(freeIndex < promptIndex, "/free must run BEFORE /prompt to give the run clean headroom.");
    }

    [Fact]
    public async Task GenerateAsync_FreeEndpointFails_StillGeneratesTheImage()
    {
        // Reclaiming memory is hygiene, not a precondition: a backend without /free (or one that is
        // momentarily busy) must never turn a generation that would have worked into a failure.
        var handler = new ScriptedHandler(FixtureImageBytes) { FailFreeCalls = true };
        var client = BuildClient(handler, new FakeStorage(new Dictionary<string, byte[]>()));

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FixtureImageBytes, result.Value.Bytes);
    }

    [Fact]
    public void Capabilities_SupportsImageConditioningTrue_MaxConditioningImagesFromSettings()
    {
        var handler = new ScriptedHandler(FixtureImageBytes);
        var client = BuildClient(handler, new FakeStorage(new Dictionary<string, byte[]>()), maxReferenceImages: "2");

        Assert.True(client.Capabilities.SupportsImageConditioning);
        Assert.Equal(2, client.Capabilities.MaxConditioningImages);
    }

    [Fact]
    public async Task GenerateAsync_NoReferenceImages_DoesNotCallUpload_ReturnsBytes()
    {
        var storage = new FakeStorage(new Dictionary<string, byte[]>());
        var handler = new ScriptedHandler(FixtureImageBytes);
        var client = BuildClient(handler, storage);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FixtureImageBytes, result.Value.Bytes);
        Assert.False(handler.UploadCalled);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ComfyUiImageModelClient BuildClient(
        HttpMessageHandler handler,
        IGeneratedAssetStorage storage,
        string maxReferenceImages = "1")
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            "comfyui",
            IntegrationConnectionMode.SystemDefault,
            new Dictionary<string, string>
            {
                ["base_url"] = "http://127.0.0.1:8188",
                ["poll_interval_ms"] = "1",
                ["max_reference_images"] = maxReferenceImages,
            },
            SelectedModel: "sd15-checkpoint.safetensors");

        return new ComfyUiImageModelClient(
            credential,
            new StaticHttpClientFactory(handler),
            storage,
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
    /// Scripts upload -&gt; /prompt -&gt; poll /history (pending then done) -&gt; /view.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly byte[] _fixtureBytes;
        private int _historyCallCount;

        public bool UploadCalled { get; private set; }

        /// <summary>Paths hit, in order, so a test can assert /free precedes /prompt.</summary>
        public List<string> CallOrder { get; } = [];

        /// <summary>When true, /free answers 500 so the best-effort contract can be exercised.</summary>
        public bool FailFreeCalls { get; set; }

        public ScriptedHandler(byte[] fixtureBytes) => _fixtureBytes = fixtureBytes;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            CallOrder.Add(path);

            if (path.EndsWith("/free", StringComparison.OrdinalIgnoreCase))
            {
                return FailFreeCalls
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : JsonResponse("{}");
            }

            if (path.Contains("/upload/image", StringComparison.OrdinalIgnoreCase))
            {
                UploadCalled = true;
                return JsonResponse("""{"name":"uploaded_ref.png","subfolder":"","type":"input"}""");
            }

            // NOTE: check "/history/" before "/prompt" — "/history/prompt-123" contains "/prompt"
            // as a substring (the prompt id happens to start with "prompt"), so the more specific
            // route must be matched first.
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
        private readonly IReadOnlyDictionary<string, byte[]> _pathToBytes;

        public FakeStorage(IReadOnlyDictionary<string, byte[]> pathToBytes) => _pathToBytes = pathToBytes;

        public Task<Result<StoredAssetStream>> OpenAsync(string storagePath, CancellationToken ct)
        {
            if (_pathToBytes.TryGetValue(storagePath, out var bytes))
            {
                return Task.FromResult(Result.Success(new StoredAssetStream(new MemoryStream(bytes), "image/jpeg", bytes.Length)));
            }

            return Task.FromResult(Result.Failure<StoredAssetStream>(StorageErrors.NotFound));
        }

        public Task<StoredAssetRef> PutAssetAsync(StoredAssetKey key, StoredAssetContent content, CancellationToken ct)
            => Task.FromResult(new StoredAssetRef("path", content.Mime, content.Bytes.Length, "hash"));
        public Task PutFolderPlaceholderAsync(string folderPath, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<StoredAssetEntry>> ListAsync(string prefix, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<StoredAssetEntry>>([]);
        public Task DeleteAsync(string storagePath, CancellationToken ct) => Task.CompletedTask;
    }
}
