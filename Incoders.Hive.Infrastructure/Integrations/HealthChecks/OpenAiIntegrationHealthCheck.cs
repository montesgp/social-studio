using System.Net.Http.Headers;
using Incoders.Hive.Application.Abstractions.Integrations;

namespace Incoders.Hive.Infrastructure.Integrations.HealthChecks;

/// <summary>
/// Real OpenAI connectivity check: GET https://api.openai.com/v1/models with a Bearer API key.
/// <c>/v1/models</c> is a free GET (no token cost), so it is the cheapest way to validate the key —
/// OpenAI does not expose a dedicated test endpoint.
/// </summary>
internal sealed class OpenAiIntegrationHealthCheck : IExternalIntegrationHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenAiIntegrationHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string ProviderKey => "openai";

    public async Task<IntegrationHealthResult> CheckAsync(
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default)
    {
        if (!IntegrationHealthCheckHttp.TryGetCredential(credentials, out var apiKey, "api_key"))
        {
            return new IntegrationHealthResult(false, ProviderKey, null, "missing_api_key", "Missing OpenAI API key.");
        }

        using var httpClient = _httpClientFactory.CreateClient(nameof(OpenAiIntegrationHealthCheck));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return await IntegrationHealthCheckHttp.SendAsync(httpClient, request, ProviderKey, cancellationToken);
    }
}
