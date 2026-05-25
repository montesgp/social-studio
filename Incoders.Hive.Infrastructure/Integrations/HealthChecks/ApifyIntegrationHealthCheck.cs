using System.Net.Http.Headers;
using Incoders.Hive.Application.Abstractions.Integrations;

namespace Incoders.Hive.Infrastructure.Integrations.HealthChecks;

/// <summary>Real Apify connectivity check: GET https://api.apify.com/v2/users/me with the API token.</summary>
internal sealed class ApifyIntegrationHealthCheck : IExternalIntegrationHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ApifyIntegrationHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string ProviderKey => "apify";

    public async Task<IntegrationHealthResult> CheckAsync(
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default)
    {
        if (!IntegrationHealthCheckHttp.TryGetCredential(credentials, out var token, "api_key", "api_token"))
        {
            return new IntegrationHealthResult(false, ProviderKey, null, "missing_api_key", "Missing Apify API token.");
        }

        using var httpClient = _httpClientFactory.CreateClient(nameof(ApifyIntegrationHealthCheck));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.apify.com/v2/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await IntegrationHealthCheckHttp.SendAsync(httpClient, request, ProviderKey, cancellationToken);
    }
}
