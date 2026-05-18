using System.Net.Http.Headers;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Infrastructure.Social.Providers;

public sealed class Data365SocialContentExtractor : ISocialContentExtractor
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;

    public Data365SocialContentExtractor(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<Result<SocialContentSnapshot>> ExtractAsync(
        SocialContentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ProviderCredentialReader.TryGet(_credential, "api_key", out var apiKey))
        {
            return Result.Failure<SocialContentSnapshot>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        using var http = _httpClientFactory.CreateClient(nameof(Data365SocialContentExtractor));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.GetAsync(
            $"https://api.data365.co/v1.1/social/url?url={Uri.EscapeDataString(request.NormalizedUrl)}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
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
