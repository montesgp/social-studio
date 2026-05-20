using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Integrations;

namespace Incoders.Hive.Infrastructure.Social.Providers;

public sealed class ApifySocialContentExtractor : ISocialContentExtractor
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;

    public ApifySocialContentExtractor(
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
        if (!ProviderCredentialReader.TryGet(_credential, "api_key", out var token)
            || !ProviderCredentialReader.TryGet(_credential, "actor_id", out var actorId))
        {
            return Result.Failure<SocialContentSnapshot>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        using var http = _httpClientFactory.CreateClient(nameof(ApifySocialContentExtractor));
        var body = JsonSerializer.Serialize(new { startUrls = new[] { new { url = request.NormalizedUrl } } });
        using var response = await http.PostAsync(
            $"https://api.apify.com/v2/acts/{Uri.EscapeDataString(actorId)}/run-sync-get-dataset-items?token={Uri.EscapeDataString(token)}",
            new StringContent(body, Encoding.UTF8, "application/json"),
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
            rawPayloadReference: "provider://apify/run-sync"));
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
