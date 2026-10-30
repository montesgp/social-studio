using System.Net;
using System.Text;
using System.Text.Json;
using SocialStudio.Application.Abstractions.Ai;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Tests.Infrastructure.Ai;

/// <summary>
/// Sysadmin-tunable sampler settings (OQ-6): <c>steps</c> / <c>cfg</c> credential keys let a
/// sysadmin trade image quality for speed in local/dev without a rebuild. Covers
/// <see cref="ComfyUiSettings.FromCredential"/> parsing/validation and that
/// <see cref="ComfyUiImageModelClient.GenerateAsync"/> actually threads the resolved values into
/// the queued workflow graph. Refs: job 8757e85a / 07771831 diagnosis.
/// </summary>
public sealed class ComfyUiSamplerSettingsTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly byte[] FixtureImageBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A];

    // ── ComfyUiSettings.FromCredential ──────────────────────────────────────

    [Fact]
    public void FromCredential_MissingStepsAndCfg_UsesVerifiedQualityDefaults()
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>()));

        Assert.True(result.IsSuccess);
        Assert.Equal(ComfyUiSettings.DefaultSteps, result.Value.Steps);
        Assert.Equal(ComfyUiSettings.DefaultCfg, result.Value.Cfg);
    }

    [Fact]
    public void FromCredential_ValidStepsAndCfgOverrides_AreHonoured()
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["steps"] = "6",
            ["cfg"] = "2.0",
        }));

        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value.Steps);
        Assert.Equal(2.0, result.Value.Cfg);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("not-a-number")]
    public void FromCredential_InvalidSteps_ReturnsProviderCredentialUnavailable(string steps)
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["steps"] = steps,
        }));

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderCredentialUnavailable.Code, result.Error.Code);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1.5")]
    [InlineData("30.1")]
    [InlineData("not-a-number")]
    public void FromCredential_InvalidCfg_ReturnsProviderCredentialUnavailable(string cfg)
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["cfg"] = cfg,
        }));

        Assert.True(result.IsFailure);
        Assert.Equal(IntegrationErrors.ProviderCredentialUnavailable.Code, result.Error.Code);
    }

    [Fact]
    public void FromCredential_CfgAtMaxBand_IsAccepted()
    {
        var result = ComfyUiSettings.FromCredential(BuildCredential(new Dictionary<string, string>
        {
            ["cfg"] = "30",
        }));

        Assert.True(result.IsSuccess);
        Assert.Equal(30.0, result.Value.Cfg);
    }

    // ── ComfyUiImageModelClient.GenerateAsync -> workflow graph ─────────────

    [Fact]
    public async Task GenerateAsync_MissingStepsAndCfgCredentials_SendsVerifiedQualityDefaultsToKSampler()
    {
        var handler = new CapturingHandler(FixtureImageBytes);
        var client = BuildClient(handler);

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (steps, cfg) = handler.ReadKSamplerStepsAndCfg();
        Assert.Equal(20, steps);
        Assert.Equal(7.0, cfg);
    }

    [Fact]
    public async Task GenerateAsync_ConfiguredStepsAndCfg_AreSentToKSampler()
    {
        var handler = new CapturingHandler(FixtureImageBytes);
        var client = BuildClient(handler, steps: "6", cfg: "2.0");

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (steps, cfg) = handler.ReadKSamplerStepsAndCfg();
        Assert.Equal(6, steps);
        Assert.Equal(2.0, cfg);
    }

    [Fact]
    public async Task GenerateAsync_InvalidStepsCredential_FailsBeforeQueueingAnyWorkflow()
    {
        var handler = new CapturingHandler(FixtureImageBytes);
        var client = BuildClient(handler, steps: "0");

        var result = await client.GenerateAsync(BuildRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(handler.CapturedPromptBody);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ComfyUiImageModelClient BuildClient(
        HttpMessageHandler handler,
        string? steps = null,
        string? cfg = null)
    {
        var values = new Dictionary<string, string>
        {
            ["base_url"] = "http://127.0.0.1:8188",
            ["poll_interval_ms"] = "1",
        };
        if (steps is not null)
        {
            values["steps"] = steps;
        }
        if (cfg is not null)
        {
            values["cfg"] = cfg;
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

    private static ResolvedProviderCredential BuildCredential(IReadOnlyDictionary<string, string> values) =>
        new(Guid.Empty, "comfyui", IntegrationConnectionMode.SystemDefault, values, SelectedModel: null);

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
    /// Same upload -&gt; /prompt -&gt; poll /history -&gt; /view script as
    /// <c>ComfyUiHappyPathTests.ScriptedHandler</c>, plus capture of the <c>/prompt</c> request body
    /// so tests can assert what actually reached the KSampler node ("8" in the embedded default
    /// template).
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly byte[] _fixtureBytes;
        private int _historyCallCount;

        public string? CapturedPromptBody { get; private set; }

        public CapturingHandler(byte[] fixtureBytes) => _fixtureBytes = fixtureBytes;

        public (int Steps, double Cfg) ReadKSamplerStepsAndCfg()
        {
            Assert.NotNull(CapturedPromptBody);
            using var doc = JsonDocument.Parse(CapturedPromptBody!);
            var ksamplerInputs = doc.RootElement.GetProperty("prompt").GetProperty("8").GetProperty("inputs");
            return (ksamplerInputs.GetProperty("steps").GetInt32(), ksamplerInputs.GetProperty("cfg").GetDouble());
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

            // NOTE: check "/history/" before "/prompt" — see ComfyUiHappyPathTests.ScriptedHandler.
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
                CapturedPromptBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);
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
