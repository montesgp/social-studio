using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Domain.Social.SingleAnalyzer;
using SocialStudio.Infrastructure.Integrations;
using SocialStudio.Infrastructure.Social.Providers.Apify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Infrastructure.Social.Providers;

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

        // Some Apify actors (e.g. apify/instagram-scraper) return a 201 with one dataset item that
        // carries the actor's own "error"/"errorDescription" instead of content — the request reached
        // the source, but the source refused full access (private, age-gated, region-locked, etc.).
        // Detect that before mapping: an adapter has no content-shaped fields to fall back to and would
        // otherwise silently produce a snapshot of nulls that fails downstream AI validation with no
        // clue as to why.
        var providerError = ReadProviderError(firstItem);
        if (providerError is not null)
        {
            _logger.LogWarning(
                "Apify extraction RESTRICTED ITEM after {ElapsedMs} ms. OrganizationId={OrganizationId} PlatformKey={PlatformKey} ActorId={ActorId} ProviderError={ProviderError} ProviderErrorDescription={ProviderErrorDescription}",
                stopwatch.ElapsedMilliseconds,
                request.OrganizationId,
                request.PlatformKey,
                actorId,
                providerError.Value.Error,
                providerError.Value.Description);

            return Result.Failure<SocialContentSnapshot>(ContentExtractionErrors.RestrictedContent);
        }

        return Result.Success(adapter.MapItem(firstItem, request));
    }

    /// <summary>
    /// Recognises a dataset item shaped as an actor-side error — <c>{ "error": "…", "errorDescription": "…" }</c>
    /// — and returns <c>null</c> for a normal content item, which never carries this pair.
    /// </summary>
    private static (string Error, string? Description)? ReadProviderError(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("error", out var errorEl)
            || errorEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var error = errorEl.GetString();
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        var description = item.TryGetProperty("errorDescription", out var descEl)
            && descEl.ValueKind == JsonValueKind.String
                ? descEl.GetString()
                : null;

        return (error, description);
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
        if (!string.IsNullOrWhiteSpace(request.ActorId))
        {
            return request.ActorId.Trim();
        }

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

            // Filter by network and capability key — no "default" fallback.
            var candidates = mappings
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.ActorId)
                    && Matches(x.NetworkKey, platformKey)
                    && Matches(x.VariantKey, capabilityKey))
                .ToList();

            if (candidates.Count == 0)
            {
                return false;
            }

            // YouTube guard: the channel-based "*youtube-shorts-scraper" cannot resolve an individual
            // video by URL (it requires a "channels" input), so a single Shorts/video URL must always
            // route to the generic youtube-scraper. Drop shorts-scraper candidates unless they are the
            // only ones configured. The generic actor handles both Shorts and regular videos by URL.
            if (Matches(platformKey, "youtube"))
            {
                var generic = candidates
                    .Where(x => !x.ActorId.Contains("shorts", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (generic.Count > 0)
                {
                    candidates = generic;
                }
            }

            // Primary first (stable sort — non-primary entries preserve their relative array order).
            var match = candidates
                .OrderByDescending(x => x.IsPrimary)
                .First();

            actorId = match.ActorId.Trim();
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
        string ActorId,
        bool IsPrimary = false);
}
