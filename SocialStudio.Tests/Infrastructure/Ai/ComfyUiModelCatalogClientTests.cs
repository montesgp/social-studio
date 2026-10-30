using System.Net;
using System.Text;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Ai.Providers;

namespace SocialStudio.Tests.Infrastructure.Ai;

/// <summary>
/// WU-5 RED tests (task 5.1): <c>ComfyUiModelCatalogClient.ListModelsAsync</c> — parses
/// <c>/object_info</c> checkpoint/IP-Adapter/LoRA enums; falls back to
/// <see cref="ComfyUiSettings.CuratedDefaultCheckpoints"/> on any failure.
/// Refs: CUI-04-a, CUI-04-b, CUI-04-c, CUI-INT-02; design §3.5, §0 OQ-3.
/// </summary>
public sealed class ComfyUiModelCatalogClientTests
{
    [Fact]
    public async Task ListModelsAsync_CheckpointEnum_ReturnsModelsTaggedAsCheckpointSource()
    {
        const string body = """
        {
            "CheckpointLoaderSimple": {
                "input": {
                    "required": {
                        "ckpt_name": [["v1-5-pruned-emaonly.safetensors", "sd_xl_base_1.0.safetensors"]]
                    }
                }
            }
        }
        """;
        var client = BuildClient(new ScriptedHandler(HttpStatusCode.OK, body));

        var result = await client.ListModelsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, model => Assert.Equal("checkpoint", model.Metadata["source"]));
        Assert.Contains(result.Value, model => model.ModelId == "v1-5-pruned-emaonly.safetensors");
        Assert.Contains(result.Value, model => model.ModelId == "sd_xl_base_1.0.safetensors");
    }

    [Fact]
    public async Task ListModelsAsync_IpAdapterEnumPresent_AppendsIpAdapterTaggedEntries()
    {
        const string body = """
        {
            "CheckpointLoaderSimple": {
                "input": { "required": { "ckpt_name": [["v1-5-pruned-emaonly.safetensors"]] } }
            },
            "IPAdapterModelLoader": {
                "input": { "required": { "ipadapter_file": [["ip-adapter_sd15.bin"]] } }
            }
        }
        """;
        var client = BuildClient(new ScriptedHandler(HttpStatusCode.OK, body));

        var result = await client.ListModelsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, model => model.ModelId == "ip-adapter_sd15.bin" && model.Metadata["source"] == "ipadapter");
        Assert.Contains(result.Value, model => model.ModelId == "v1-5-pruned-emaonly.safetensors" && model.Metadata["source"] == "checkpoint");
    }

    [Fact]
    public async Task ListModelsAsync_EmptyCheckpointEnum_ReturnsEmptyListNoError()
    {
        const string body = """
        {
            "CheckpointLoaderSimple": {
                "input": { "required": { "ckpt_name": [[]] } }
            }
        }
        """;
        var client = BuildClient(new ScriptedHandler(HttpStatusCode.OK, body));

        var result = await client.ListModelsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task ListModelsAsync_ServerReturnsHttpError_FallsBackToCuratedDefaultCheckpoints()
    {
        var client = BuildClient(new ScriptedHandler(HttpStatusCode.InternalServerError, "server error"));

        var result = await client.ListModelsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ComfyUiSettings.CuratedDefaultCheckpoints.Count, result.Value.Count);
        Assert.All(result.Value, model => Assert.Equal("curated", model.Metadata["source"]));
        Assert.All(result.Value, model => Assert.Contains(model.ModelId, ComfyUiSettings.CuratedDefaultCheckpoints));
    }

    [Fact]
    public async Task ListModelsAsync_HandlerThrows_FallsBackToCuratedDefaultCheckpoints_NoException()
    {
        var client = BuildClient(new ThrowingHandler());

        var result = await client.ListModelsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ComfyUiSettings.CuratedDefaultCheckpoints.Count, result.Value.Count);
        Assert.All(result.Value, model => Assert.Equal("curated", model.Metadata["source"]));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ComfyUiModelCatalogClient BuildClient(HttpMessageHandler handler)
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            "comfyui",
            IntegrationConnectionMode.SystemDefault,
            new Dictionary<string, string> { ["base_url"] = "http://127.0.0.1:8188" },
            SelectedModel: null);

        return new ComfyUiModelCatalogClient(credential, new StaticHttpClientFactory(handler));
    }

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public ScriptedHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StaticHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
