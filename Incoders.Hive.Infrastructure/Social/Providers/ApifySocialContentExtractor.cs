using System.Diagnostics;
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
        var url = $"https://api.apify.com/v2/acts/{Uri.EscapeDataString(actorId)}/run-sync-get-dataset-items?token=***";
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Apify extraction START. OrganizationId={OrganizationId} PlatformKey={PlatformKey} CapabilityKey={CapabilityKey} ActorId={ActorId} AdapterPlatform={Adapter} Url={Url} RequestBytes={RequestBytes} RequestPreview={RequestPreview}",
            request.OrganizationId,
            request.PlatformKey,
            request.CapabilityKey,
            actorId,
            adapter.PlatformKey,
            url,
            body.Length,
            Preview(body, 600));

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
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Apify extraction TIMED OUT after {ElapsedMs} ms. OrganizationId={OrganizationId} PlatformKey={PlatformKey} ActorId={ActorId}. The HttpClient's per-request timeout (120s for this client) fired or the operation was aborted.",
                stopwatch.ElapsedMilliseconds,
                request.OrganizationId,
                request.PlatformKey,
                actorId);

            return Result.Failure<SocialContentSnapshot>(IntegrationErrors.ProviderTimeout);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Apify extraction TRANSPORT FAILURE after {ElapsedMs} ms (could not reach the provider). OrganizationId={OrganizationId} PlatformKey={PlatformKey} ActorId={ActorId} ExceptionType={ExceptionType} Message={Message} InnerType={InnerType} InnerMessage={InnerMessage}",
                stopwatch.ElapsedMilliseconds,
                request.OrganizationId,
                request.PlatformKey,
                actorId,
                ex.GetType().FullName,
                ex.Message,
                ex.InnerException?.GetType().FullName,
                ex.InnerException?.Message);

            return Result.Failure<SocialContentSnapshot>(IntegrationErrors.ExternalProviderUnavailable);
        }

        using var responseHandle = response;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Apify extraction RESPONSE ERROR after {ElapsedMs} ms. OrganizationId={OrganizationId} PlatformKey={PlatformKey} CapabilityKey={CapabilityKey} ActorId={ActorId} StatusCode={StatusCode} ReasonPhrase={ReasonPhrase} ResponseBytes={ResponseBytes} BodyPreview={BodyPreview}",
                stopwatch.ElapsedMilliseconds,
                request.OrganizationId,
                request.PlatformKey,
                request.CapabilityKey,
                actorId,
                (int)response.StatusCode,
                response.ReasonPhrase,
                payload.Length,
                Preview(payload, 1500));

            return Result.Failure<SocialContentSnapshot>(MapStatus(response));
        }

        // Parse the array and take the first item
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "[]" : payload);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            _logger.LogWarning(
                "Apify extraction EMPTY DATASET after {ElapsedMs} ms. OrganizationId={OrganizationId} PlatformKey={PlatformKey} CapabilityKey={CapabilityKey} ActorId={ActorId} ResponseBytes={ResponseBytes} BodyPreview={BodyPreview}",
                stopwatch.ElapsedMilliseconds,
                request.OrganizationId,
                request.PlatformKey,
                request.CapabilityKey,
                actorId,
                payload.Length,
                Preview(payload, 800));

            return Result.Failure<SocialContentSnapshot>(ContentExtractionErrors.EmptyResponse);
        }

        var itemCount = doc.RootElement.GetArrayLength();
        _logger.LogInformation(
            "Apify extraction SUCCESS after {ElapsedMs} ms. OrganizationId={OrganizationId} PlatformKey={PlatformKey} ActorId={ActorId} StatusCode={StatusCode} ResponseBytes={ResponseBytes} ItemCount={ItemCount}",
            stopwatch.ElapsedMilliseconds,
            request.OrganizationId,
            request.PlatformKey,
            actorId,
            (int)response.StatusCode,
            payload.Length,
            itemCount);

        var firstItem = doc.RootElement[0];
        return Result.Success(adapter.MapItem(firstItem, request));
    }

    private static string Preview(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        return text.Length <= max ? text : text[..max] + "…(truncated)";
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

        return null;
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
