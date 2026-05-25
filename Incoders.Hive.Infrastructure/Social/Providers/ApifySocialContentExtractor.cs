using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Social.Providers;

internal sealed class ApifySocialContentExtractor : ISocialContentExtractor
{
    private readonly ResolvedProviderCredential _credential;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IReadOnlyList<ISocialPlatformScrapeAdapter> _adapters;
    private readonly ILogger<ApifySocialContentExtractor> _logger;

    private static readonly ISocialPlatformScrapeAdapter DefaultFallback = new DefaultApifyScrapeAdapter();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ApifySocialContentExtractor(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        IEnumerable<ISocialPlatformScrapeAdapter> adapters,
        ILogger<ApifySocialContentExtractor>? logger = null)
    {
        _credential = credential;
        _httpClientFactory = httpClientFactory;
        _adapters = adapters.ToList();
        _logger = logger ?? NullLogger<ApifySocialContentExtractor>.Instance;
    }

    public async Task<Result<SocialContentSnapshot>> ExtractAsync(
        SocialContentRequest request,
        CancellationToken cancellationToken = default)
    {
        var hasApiKey = ProviderCredentialReader.TryGet(_credential, "api_key", out var token)
            || ProviderCredentialReader.TryGet(_credential, "api_token", out token);

        var actorId = ResolveActorId(request);
        if (!hasApiKey || string.IsNullOrWhiteSpace(actorId))
        {
            _logger.LogWarning(
                "Apify extraction cannot start because required configuration is missing. OrganizationId={OrganizationId} PlatformKey={PlatformKey} CapabilityKey={CapabilityKey} HasApiKey={HasApiKey} HasActorId={HasActorId}",
                request.OrganizationId,
                request.PlatformKey,
                request.CapabilityKey,
                hasApiKey,
                !string.IsNullOrWhiteSpace(actorId));

            return Result.Failure<SocialContentSnapshot>(IntegrationErrors.ProviderCredentialUnavailable);
        }

        // Resolve adapter by platform key; fall back to the default adapter
        var adapter = ResolveAdapter(request.PlatformKey);

        using var http = _httpClientFactory.CreateClient(nameof(ApifySocialContentExtractor));
        var body = JsonSerializer.Serialize(adapter.BuildActorInput(request), JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(
                $"https://api.apify.com/v2/acts/{Uri.EscapeDataString(actorId)}/run-sync-get-dataset-items?token={Uri.EscapeDataString(token)}",
                new StringContent(body, Encoding.UTF8, "application/json"),
                cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Apify extraction timed out before the run-sync call returned. OrganizationId={OrganizationId} PlatformKey={PlatformKey} ActorId={ActorId}",
                request.OrganizationId,
                request.PlatformKey,
                actorId);

            return Result.Failure<SocialContentSnapshot>(IntegrationErrors.ProviderTimeout);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Apify extraction could not reach the provider. OrganizationId={OrganizationId} PlatformKey={PlatformKey} ActorId={ActorId}",
                request.OrganizationId,
                request.PlatformKey,
                actorId);

            return Result.Failure<SocialContentSnapshot>(IntegrationErrors.ExternalProviderUnavailable);
        }

        using var responseHandle = response;

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Apify extraction failed with external status. OrganizationId={OrganizationId} PlatformKey={PlatformKey} CapabilityKey={CapabilityKey} ActorId={ActorId} StatusCode={StatusCode} ReasonPhrase={ReasonPhrase}",
                request.OrganizationId,
                request.PlatformKey,
                request.CapabilityKey,
                actorId,
                (int)response.StatusCode,
                response.ReasonPhrase);

            return Result.Failure<SocialContentSnapshot>(MapStatus(response));
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        // Parse the array and take the first item
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "[]" : payload);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            _logger.LogWarning(
                "Apify extraction returned an empty dataset. OrganizationId={OrganizationId} PlatformKey={PlatformKey} CapabilityKey={CapabilityKey} ActorId={ActorId}",
                request.OrganizationId,
                request.PlatformKey,
                request.CapabilityKey,
                actorId);

            return Result.Failure<SocialContentSnapshot>(ContentExtractionErrors.EmptyResponse);
        }

        var firstItem = doc.RootElement[0];
        return Result.Success(adapter.MapItem(firstItem, request));
    }

    private ISocialPlatformScrapeAdapter ResolveAdapter(string platformKey) =>
        _adapters.FirstOrDefault(a => string.Equals(a.PlatformKey, platformKey, StringComparison.OrdinalIgnoreCase))
        ?? _adapters.FirstOrDefault(a => a.PlatformKey == "default")
        ?? DefaultFallback;

    private static Error MapStatus(HttpResponseMessage response) =>
        (int)response.StatusCode switch
        {
            401 or 403 => IntegrationErrors.ProviderCredentialUnavailable,
            408 => IntegrationErrors.ProviderTimeout,
            429 => IntegrationErrors.ProviderRateLimited,
            >= 500 => IntegrationErrors.ExternalProviderUnavailable,
            _ => IntegrationErrors.ExternalProviderUnavailable,
        };

    private string? ResolveActorId(SocialContentRequest request)
    {
        if (ProviderCredentialReader.TryGet(_credential, "actor_mappings_json", out var json)
            && TryResolveMappedActor(json, request.PlatformKey, request.CapabilityKey, out var mappedActorId))
        {
            return mappedActorId;
        }

        return ProviderCredentialReader.TryGet(_credential, "actor_id", out var fallbackActorId)
            ? fallbackActorId
            : null;
    }

    private static bool TryResolveMappedActor(
        string json,
        string platformKey,
        string capabilityKey,
        out string actorId)
    {
        actorId = string.Empty;

        try
        {
            var mappings = JsonSerializer.Deserialize<List<ApifyActorMapping>>(json, JsonOptions) ?? [];
            var candidates = mappings
                .Where(x => !string.IsNullOrWhiteSpace(x.ActorId) && Matches(x.NetworkKey, platformKey))
                .ToList();
            var match = candidates.FirstOrDefault(x => Matches(x.VariantKey, capabilityKey))
                ?? candidates.FirstOrDefault(x => Matches(x.VariantKey, "default"))
                ?? candidates.FirstOrDefault();

            actorId = match?.ActorId?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(actorId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool Matches(string? value, string expected) =>
        string.Equals(value?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record ApifyActorMapping(
        string NetworkKey,
        string VariantKey,
        string ActorId);
}
