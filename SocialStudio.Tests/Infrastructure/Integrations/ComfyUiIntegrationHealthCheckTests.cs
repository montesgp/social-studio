using System.Net;
using SocialStudio.Infrastructure.Integrations.HealthChecks;

namespace SocialStudio.Tests.Infrastructure.Integrations;

/// <summary>
/// WU-6 RED tests (task 6.2): <c>ComfyUiIntegrationHealthCheck</c> probes
/// <c>GET {base_url}/system_stats</c>. Refs: CUI-INT-03-a, CUI-INT-03-b; design §2.2, §4.
/// </summary>
public sealed class ComfyUiIntegrationHealthCheckTests
{
    [Fact]
    public void ProviderKey_IsComfyUi()
    {
        var check = new ComfyUiIntegrationHealthCheck(new StaticHttpClientFactory(new ScriptedHandler(HttpStatusCode.OK)));

        Assert.Equal("comfyui", check.ProviderKey);
    }

    [Fact]
    public async Task ServerReturnsHttp200_ReportsConnected()
    {
        var check = new ComfyUiIntegrationHealthCheck(new StaticHttpClientFactory(new ScriptedHandler(HttpStatusCode.OK)));

        var result = await check.CheckAsync(
            new Dictionary<string, string> { ["base_url"] = "http://127.0.0.1:8188" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("comfyui", result.Provider);
    }

    [Fact]
    public async Task ServerReturnsNon2xx_ReportsFailedWithDetail()
    {
        var check = new ComfyUiIntegrationHealthCheck(new StaticHttpClientFactory(new ScriptedHandler(HttpStatusCode.ServiceUnavailable)));

        var result = await check.CheckAsync(
            new Dictionary<string, string> { ["base_url"] = "http://127.0.0.1:8188" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task ServerUnreachable_ReportsFailedWithConnectivityDetail()
    {
        var check = new ComfyUiIntegrationHealthCheck(new StaticHttpClientFactory(new ThrowingHandler()));

        var result = await check.CheckAsync(
            new Dictionary<string, string> { ["base_url"] = "http://127.0.0.1:8188" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorCode);
    }

    // -----------------------------------------------------------------------
    // Test doubles
    // -----------------------------------------------------------------------

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public ScriptedHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode));
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
