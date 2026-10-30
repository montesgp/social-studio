using System.Net.Http.Headers;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Infrastructure.Social.Providers;

public sealed class Data365SocialContentExtractor : ISocialContentExtractor
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<Data365SocialContentExtractor> _logger;

    public Data365SocialContentExtractor(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        ILogger<Data365SocialContentExtractor>? logger = null)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<Data365SocialContentExtractor>.Instance;
    }

    public async Task<Result<SocialContentSnapshot>> ExtractAsync(
        SocialContentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderCredentialReader.TryGet(_credential, "api_key", out var apiKey))
        {
            _logger.LogWarning(
                "Data365 extraction cannot start because the API key is missing. OrganizationId={OrganizationId} PlatformKey={PlatformKey} CapabilityKey={CapabilityKey}",
                request.OrganizationId,
                request.PlatformKey,
                request.CapabilityKey);

            return Result.Failure<SocialContentSnapshot>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        using var http = _httpClientFactory.CreateClient(nameof(Data365SocialContentExtractor));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.GetAsync(
            $"https://api.data365.co/v1.1/social/url?url={Uri.EscapeDataString(request.NormalizedUrl)}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Data365 extraction failed with external status. OrganizationId={OrganizationId} PlatformKey={PlatformKey} CapabilityKey={CapabilityKey} StatusCode={StatusCode} ReasonPhrase={ReasonPhrase}",
                request.OrganizationId,
                request.PlatformKey,
                request.CapabilityKey,
                (int)response.StatusCode,
                response.ReasonPhrase);

            return Result.Failure<SocialContentSnapshot>(MapStatus(response));
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return Result.Success(SocialSnapshotMapper.FromJson(
            request.PlatformKey,
            request.NormalizedUrl,
            payload,
            rawPayloadReference: "provider://data365/url"));
    }

    private static Error MapStatus(HttpResponseMessage response) =>
        (int)response.StatusCode switch
        {
            401 or 403 => IntegrationErrors.ProviderCredentialUnavailable,
            408 => IntegrationErrors.ProviderTimeout,
            429 => IntegrationErrors.ProviderRateLimited,
            >= 500 => IntegrationErrors.ExternalProviderUnavailable,
            _ => IntegrationErrors.ExternalProviderUnavailable,
        };
}
