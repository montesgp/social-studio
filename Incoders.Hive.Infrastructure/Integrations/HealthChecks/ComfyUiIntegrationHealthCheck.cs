using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Ai.Providers;

namespace Incoders.Hive.Infrastructure.Integrations.HealthChecks;

/// <summary>
/// Real ComfyUI connectivity check: <c>GET {base_url}/system_stats</c> against the configured local
/// server. HTTP 200 reports <c>Connected</c>; a non-2xx response or an unreachable server reports
/// <c>Failed</c> with a connectivity detail — informational only, mirrors
/// <see cref="ClaudePromptCliIntegrationHealthCheck"/>. Refs: CUI-INT-03-a, CUI-INT-03-b; design §2.2, §4.
/// </summary>
internal sealed class ComfyUiIntegrationHealthCheck : IExternalIntegrationHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ComfyUiIntegrationHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public string ProviderKey => ComfyUiSettings.ProviderKey;

    public async Task<IntegrationHealthResult> CheckAsync(
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default)
    {
        var credential = new ResolvedProviderCredential(
            Guid.Empty,
            ProviderKey,
            IntegrationConnectionMode.SystemDefault,
            credentials,
            SelectedModel: null);

        var settings = ComfyUiSettings.FromCredential(credential);
        if (settings.IsFailure)
        {
            return new IntegrationHealthResult(false, ProviderKey, null, settings.Error.Code, "Invalid ComfyUI server configuration.");
        }

        using var httpClient = _httpClientFactory.CreateClient(nameof(ComfyUiIntegrationHealthCheck));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{settings.Value.BaseUrl.TrimEnd('/')}/system_stats");

        return await IntegrationHealthCheckHttp.SendAsync(httpClient, request, ProviderKey, cancellationToken);
    }
}
