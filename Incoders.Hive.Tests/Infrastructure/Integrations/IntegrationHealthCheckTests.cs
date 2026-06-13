using System.Net;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Integrations.HealthChecks;

namespace Incoders.Hive.Tests.Infrastructure.Integrations;

public sealed class IntegrationHealthCheckTests
{
    [Fact]
    public async Task Apify_Success_HitsUsersMeWithBearerToken()
    {
        var stub = new StubHandler(HttpStatusCode.OK);
        var check = new ApifyIntegrationHealthCheck(new StubHttpClientFactory(stub));

        var result = await check.CheckAsync(
            new Dictionary<string, string> { ["api_key"] = "apify_api_token" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("apify", result.Provider);
        Assert.Equal("https://api.apify.com/v2/users/me", stub.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", stub.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("apify_api_token", stub.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Apify_401_ReturnsInvalidCredentials()
    {
        var check = new ApifyIntegrationHealthCheck(new StubHttpClientFactory(new StubHandler(HttpStatusCode.Unauthorized)));

        var result = await check.CheckAsync(
            new Dictionary<string, string> { ["api_key"] = "bad" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal("invalid_credentials", result.ErrorCode);
    }

    [Fact]
    public async Task Apify_MissingToken_ReturnsMissingApiKey()
    {
        var check = new ApifyIntegrationHealthCheck(new StubHttpClientFactory(new StubHandler(HttpStatusCode.OK)));

        var result = await check.CheckAsync(new Dictionary<string, string>(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("missing_api_key", result.ErrorCode);
    }

    [Fact]
    public async Task Claude_Success_HitsModelsWithApiKeyAndVersionHeaders()
    {
        var stub = new StubHandler(HttpStatusCode.OK);
        var check = new ClaudeIntegrationHealthCheck(new StubHttpClientFactory(stub));

        var result = await check.CheckAsync(
            new Dictionary<string, string> { ["api_key"] = "sk-ant-key" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("claude", result.Provider);
        Assert.Equal("https://api.anthropic.com/v1/models", stub.LastRequest!.RequestUri!.ToString());
        Assert.Equal("sk-ant-key", Single(stub.LastRequest, "x-api-key"));
        Assert.Equal("2023-06-01", Single(stub.LastRequest, "anthropic-version"));
    }

    [Fact]
    public async Task Claude_401_ReturnsInvalidCredentials()
    {
        var check = new ClaudeIntegrationHealthCheck(new StubHttpClientFactory(new StubHandler(HttpStatusCode.Unauthorized)));

        var result = await check.CheckAsync(
            new Dictionary<string, string> { ["api_key"] = "bad" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_credentials", result.ErrorCode);
    }

    [Fact]
    public async Task OpenAi_Success_HitsModelsWithBearerToken()
    {
        var stub = new StubHandler(HttpStatusCode.OK);
        var check = new OpenAiIntegrationHealthCheck(new StubHttpClientFactory(stub));

        var result = await check.CheckAsync(
            new Dictionary<string, string> { ["api_key"] = "sk-openai-key" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("openai", result.Provider);
        Assert.Equal("https://api.openai.com/v1/models", stub.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", stub.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("sk-openai-key", stub.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task OpenAi_401_ReturnsInvalidCredentials()
    {
        var check = new OpenAiIntegrationHealthCheck(new StubHttpClientFactory(new StubHandler(HttpStatusCode.Unauthorized)));

        var result = await check.CheckAsync(
            new Dictionary<string, string> { ["api_key"] = "bad" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_credentials", result.ErrorCode);
    }

    [Fact]
    public async Task Tester_DelegatesToHealthCheck_AndSurfacesFailure()
    {
        var provider = ApifyProvider();
        var tester = new DefaultIntegrationConnectionTester(new IExternalIntegrationHealthCheck[]
        {
            new FakeHealthCheck("apify", new IntegrationHealthResult(false, "apify", 401, "invalid_credentials", "Apify rejected the credentials (HTTP 401)."))
        });

        var result = await tester.TestAsync(
            provider,
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "x" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("401", result.ErrorMessage);
    }

    [Fact]
    public async Task Tester_FailsWhenRequiredCredentialMissing()
    {
        var tester = new DefaultIntegrationConnectionTester();

        var result = await tester.TestAsync(
            ApifyProvider(),
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("api_key", result.ErrorMessage);
    }

    [Fact]
    public async Task Tester_SucceedsWhenNoHealthCheckRegisteredForProvider()
    {
        var tester = new DefaultIntegrationConnectionTester();

        var result = await tester.TestAsync(
            ApifyProvider(),
            IntegrationConnectionMode.BringYourOwnKey,
            new Dictionary<string, string> { ["api_key"] = "x" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private static IntegrationProvider ApifyProvider() =>
        IntegrationProvider.Create(
            new IntegrationProviderId(Guid.NewGuid()),
            "apify",
            "Apify",
            IntegrationProviderCategory.DataExtraction,
            "Content discovery and scraping provider.",
            "apify",
            true,
            false,
            false,
            false,
            new[] { new IntegrationCredentialField("api_key", "API key", true, true, null, null) },
            Array.Empty<string>(),
            DateTime.UtcNow).Value;

    private static string? Single(HttpRequestMessage request, string header) =>
        request.Headers.TryGetValues(header, out var values) ? values.FirstOrDefault() : null;

    private sealed class FakeHealthCheck : IExternalIntegrationHealthCheck
    {
        private readonly IntegrationHealthResult _result;

        public FakeHealthCheck(string providerKey, IntegrationHealthResult result)
        {
            ProviderKey = providerKey;
            _result = result;
        }

        public string ProviderKey { get; }

        public Task<IntegrationHealthResult> CheckAsync(
            IReadOnlyDictionary<string, string> credentials,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public StubHandler(HttpStatusCode status) => _status = status;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent("{}"),
            });
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
