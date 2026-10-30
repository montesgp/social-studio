using SocialStudio.Application.Abstractions.Integrations;

namespace SocialStudio.Infrastructure.Integrations.HealthChecks;

/// <summary>
/// Real Anthropic/Claude connectivity check: GET https://api.anthropic.com/v1/models
/// with x-api-key + anthropic-version headers.
/// </summary>
internal sealed class ClaudeIntegrationHealthCheck : IExternalIntegrationHealthCheck
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly IHttpClientFactory _httpClientFactory;

    public ClaudeIntegrationHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string ProviderKey => "claude";

    public async Task<IntegrationHealthResult> CheckAsync(
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default)
    {
        if (!IntegrationHealthCheckHttp.TryGetCredential(credentials, out var apiKey, "api_key"))
        {
            return new IntegrationHealthResult(false, ProviderKey, null, "missing_api_key", "Missing Anthropic API key.");
        }

        using var httpClient = _httpClientFactory.CreateClient(nameof(ClaudeIntegrationHealthCheck));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);

        return await IntegrationHealthCheckHttp.SendAsync(httpClient, request, ProviderKey, cancellationToken);
    }
}
