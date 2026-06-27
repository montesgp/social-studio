using System.Net;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;

namespace Incoders.Hive.Tests.Infrastructure.Ai;

public sealed class ClaudePromptCliModelCatalogClientTests
{
    [Fact]
    public async Task NoApiKey_ReturnsCuratedCurrentModels()
    {
        var client = new ClaudePromptCliModelCatalogClient(
            BuildCredential(),
            new StubHttpClientFactory(new StubHandler(HttpStatusCode.OK, "{}")),
            apiKeyFromEnvironment: () => null);

        var result = await client.ListModelsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, m => m.ModelId == "claude-opus-4-8");
        Assert.Equal(ClaudePromptCliSettings.DefaultTextModels.Count, result.Value.Count);
    }

    [Fact]
    public async Task WithApiKey_ListsLiveModelsFromAnthropic()
    {
        const string payload = "{\"data\":[{\"id\":\"claude-opus-4-8\",\"display_name\":\"Claude Opus 4.8\"},{\"id\":\"claude-future-9\",\"display_name\":\"Future\"}]}";
        var stub = new StubHandler(HttpStatusCode.OK, payload);
        var client = new ClaudePromptCliModelCatalogClient(
            BuildCredential(("api_key", "sk-ant-key")),
            new StubHttpClientFactory(stub),
            apiKeyFromEnvironment: () => null);

        var result = await client.ListModelsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, m => m.ModelId == "claude-future-9");
        Assert.Equal("https://api.anthropic.com/v1/models", stub.LastRequest!.RequestUri!.ToString());
        Assert.Equal("sk-ant-key", Single(stub.LastRequest, "x-api-key"));
    }

    [Fact]
    public async Task UsesEnvironmentApiKeyWhenCredentialMissing()
    {
        var stub = new StubHandler(HttpStatusCode.OK, "{\"data\":[{\"id\":\"claude-opus-4-8\"}]}");
        var client = new ClaudePromptCliModelCatalogClient(
            BuildCredential(),
            new StubHttpClientFactory(stub),
            apiKeyFromEnvironment: () => "sk-ant-env");

        var result = await client.ListModelsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("sk-ant-env", Single(stub.LastRequest!, "x-api-key"));
    }

    [Fact]
    public async Task LiveFailure_FallsBackToCuratedModels()
    {
        var client = new ClaudePromptCliModelCatalogClient(
            BuildCredential(("api_key", "bad")),
            new StubHttpClientFactory(new StubHandler(HttpStatusCode.Unauthorized, "{}")),
            apiKeyFromEnvironment: () => null);

        var result = await client.ListModelsAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, m => m.ModelId == "claude-opus-4-8");
    }

    private static ResolvedProviderCredential BuildCredential(params (string Key, string Value)[] values) =>
        new(Guid.Empty, "claude-p", IntegrationConnectionMode.ManagedByIncoders,
            values.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase), SelectedModel: null);

    private static string? Single(HttpRequestMessage request, string header) =>
        request.Headers.TryGetValues(header, out var v) ? v.FirstOrDefault() : null;

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
