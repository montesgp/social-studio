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
/// WU-4 RED tests (tasks 4.2, 4.3): <c>ComfyUiImageModelClient.GenerateAsync</c> honours
/// <see cref="ImageGenerationRequest.BackgroundOnly"/> by injecting a directive into the positive
/// <c>CLIPTextEncode</c> prompt (KSampler's positive input, node "2" in the bundled default
/// template) mirroring <c>OpenAiImageModelClient</c>'s directive construction/placement, and
/// <c>DefaultNegativePrompt</c> carries the added anatomy-defect suppression terms alongside every
/// pre-existing text-suppression term. Refs: CAGH-CUI-01-a, CAGH-CUI-01-b, CAGH-CUI-02-a,
/// CAGH-CUI-02-b; design D5, D6.
/// </summary>
public sealed class ComfyUiImageModelClientTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly byte[] FixtureImageBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A];

    private static readonly string[] PreExistingNegativeTerms =
    [
        "text", "letters", "words", "typography", "watermark", "signature", "caption", "logo",
        "label", "subtitles", "writing", "font", "handwriting", "numbers", "low quality",
        "blurry", "deformed", "distorted",
    ];

    private static readonly string[] NewAnatomyTerms =
    [
        "extra limbs", "extra fingers", "missing fingers", "fused fingers", "bad hands",
        "malformed hands", "deformed face", "duplicate face", "cloned face", "two heads",
        "extra person", "mutated", "disfigured", "bad anatomy",
    ];

    [Fact]
    public async Task GenerateAsync_BackgroundOnlyTrue_InjectsDirectiveIntoPositivePrompt()
    {
        var handler = new CapturingHandler(FixtureImageBytes);
        var client = BuildClient(handler);

        var request = BuildRequest() with { BackgroundOnly = true };

        var result = await client.GenerateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var positiveText = handler.CapturePositivePromptText();
        Assert.StartsWith(
            "generate ONLY the background", positiveText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(request.Prompt, positiveText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_BackgroundOnlyFalse_LeavesPromptIdenticalToPreChange()
    {
        var handler = new CapturingHandler(FixtureImageBytes);
        var client = BuildClient(handler);

        var request = BuildRequest() with { BackgroundOnly = false };

        var result = await client.GenerateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var positiveText = handler.CapturePositivePromptText();
        Assert.Equal(request.Prompt, positiveText);
    }

    [Fact]
    public async Task GenerateAsync_BackgroundOnlyUnset_LeavesPromptIdenticalToPreChange()
    {
        var handler = new CapturingHandler(FixtureImageBytes);
        var client = BuildClient(handler);

        var request = BuildRequest();

        var result = await client.GenerateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var positiveText = handler.CapturePositivePromptText();
        Assert.Equal(request.Prompt, positiveText);
    }

    [Fact]
    public async Task GenerateAsync_NegativePrompt_RetainsEveryPreExistingTextSuppressionTerm()
    {
        var handler = new CapturingHandler(FixtureImageBytes);
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var negativeText = handler.CaptureNegativePromptText();
        foreach (var term in PreExistingNegativeTerms)
        {
            Assert.Contains(term, negativeText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task GenerateAsync_NegativePrompt_IncludesEveryNewAnatomyDefectTerm()
    {
        var handler = new CapturingHandler(FixtureImageBytes);
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var negativeText = handler.CaptureNegativePromptText();
        foreach (var term in NewAnatomyTerms)
        {
            Assert.Contains(term, negativeText, StringComparison.OrdinalIgnoreCase);
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ComfyUiImageModelClient BuildClient(HttpMessageHandler handler)
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            "comfyui",
            IntegrationConnectionMode.SystemDefault,
            new Dictionary<string, string>
            {
                ["base_url"] = "http://127.0.0.1:8188",
                ["poll_interval_ms"] = "1",
                ["max_reference_images"] = "1",
            },
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
    /// Scripts upload -&gt; /prompt (captures the queued graph body) -&gt; poll /history -&gt; /view.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly byte[] _fixtureBytes;
        private int _historyCallCount;
        private string? _capturedPromptBody;

        public CapturingHandler(byte[] fixtureBytes) => _fixtureBytes = fixtureBytes;

        public string CapturePositivePromptText() => ExtractNodeText("2");

        public string CaptureNegativePromptText() => ExtractNodeText("3");

        private string ExtractNodeText(string nodeId)
        {
            Assert.NotNull(_capturedPromptBody);
            using var doc = JsonDocument.Parse(_capturedPromptBody!);
            return doc.RootElement
                .GetProperty("prompt")
                .GetProperty(nodeId)
                .GetProperty("inputs")
                .GetProperty("text")
                .GetString() ?? string.Empty;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/free", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("{}");
            }

            if (path.Contains("/upload/image", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{"name":"uploaded_ref.png","subfolder":"","type":"input"}""");
            }

            // NOTE: check "/history/" before "/prompt" — the prompt id happens to start with
            // "prompt", so the more specific route must be matched first.
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
                _capturedPromptBody = await request.Content!.ReadAsStringAsync(cancellationToken);
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
