using SocialStudio.Application.Abstractions.Integrations;

namespace SocialStudio.Infrastructure.Integrations.HealthChecks;

/// <summary>Shared helpers for provider health checks: credential lookup + HTTP response mapping.</summary>
internal static class IntegrationHealthCheckHttp
{
    public static bool TryGetCredential(
        IReadOnlyDictionary<string, string> credentials,
        out string value,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (credentials.TryGetValue(key, out var candidate) && !string.IsNullOrWhiteSpace(candidate))
            {
                value = candidate.Trim();
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public static async Task<IntegrationHealthResult> SendAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        string provider,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return new IntegrationHealthResult(true, provider, statusCode, null, "Connection OK.");
            }

            var errorCode = statusCode switch
            {
                401 or 403 => "invalid_credentials",
                429 => "rate_limited",
                >= 500 => "provider_unavailable",
                _ => "unexpected_status",
            };

            var message = statusCode is 401 or 403
                ? $"{provider} rejected the credentials (HTTP {statusCode})."
                : $"{provider} returned HTTP {statusCode}.";

            return new IntegrationHealthResult(false, provider, statusCode, errorCode, message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new IntegrationHealthResult(false, provider, null, "unreachable", $"Could not reach {provider}.");
        }
    }
}
