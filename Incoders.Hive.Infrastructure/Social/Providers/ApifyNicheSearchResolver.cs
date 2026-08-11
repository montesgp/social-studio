using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Social.Providers;

/// <summary>
/// Infrastructure implementation of <see cref="INicheSearchResolver"/>.
///
/// Actor resolution order (design §8.1, the ONLY way an actor id enters this process):
///   1. <see cref="IWorkflowConfigurationResolver.ResolveStepAsync"/> for
///      (single-analyzer, content-discovery, discover-niche-content, niche-search, networkKey).
///   2. If empty/no enabled route: fall back to <see cref="ApifyActorMappingLookup.GetAllMappedActors"/>
///      over <c>actor_mappings_json</c> — still configuration, exactly
///      <c>ApifyProfileScrapeResolver.cs:146-151</c>.
///   3. Still empty → <see cref="SourceOutcome.Unconfigured"/>. Never a literal id, never a throw
///      (CD-12/CD-13, D12/D13).
///
/// Only <c>tiktok</c> has a working adapter today (<see cref="TikTokNicheSearchAdapter"/>) — any other
/// network degrades to <see cref="SourceOutcome.Unconfigured"/> rather than crashing.
/// </summary>
public sealed class ApifyNicheSearchResolver : INicheSearchResolver
{
    private const string ApifyProviderKey = "apify";
    private const string CapabilityKey = "niche-search";
    private const string WorkflowKey = "single-analyzer";
    private const string AnalysisType = "content-discovery";
    private const string StepKey = "discover-niche-content";
    private const string HttpClientName = nameof(ApifyNicheSearchResolver);
    private const string SupportedNetworkKey = "tiktok";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly IIntegrationResolver _integrationResolver;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ApifyNicheSearchResolver> _logger;
    private readonly TikTokNicheSearchAdapter _adapter = new();

    /// <summary>Production constructor — resolves credentials per request via <see cref="IIntegrationResolver"/>.</summary>
    public ApifyNicheSearchResolver(
        IWorkflowConfigurationResolver workflowResolver,
        IIntegrationResolver integrationResolver,
        IHttpClientFactory httpClientFactory,
        ILogger<ApifyNicheSearchResolver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(workflowResolver);
        ArgumentNullException.ThrowIfNull(integrationResolver);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _workflowResolver = workflowResolver;
        _integrationResolver = integrationResolver;
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<ApifyNicheSearchResolver>.Instance;
    }

    /// <summary>Test constructor — accepts a pre-resolved credential to avoid I/O in unit tests.</summary>
    internal ApifyNicheSearchResolver(
        IWorkflowConfigurationResolver workflowResolver,
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        ILogger<ApifyNicheSearchResolver>? logger = null)
        : this(workflowResolver, new PreResolvedIntegrationResolver(credential), httpClientFactory, logger)
    {
    }

    public async Task<NicheSearchResolution> SearchAsync(NicheSearchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.NetworkKey, SupportedNetworkKey, StringComparison.OrdinalIgnoreCase))
        {
            // No adapter exists for this network yet (design §8.1 network-widening note) — a network
            // configured without a working adapter degrades through Unconfigured, never a crash.
            return new NicheSearchResolution(SourceOutcome.Unconfigured, []);
        }

        var actorIds = await ResolveActorIdsAsync(request, ct);
        if (actorIds.Count == 0)
        {
            _logger.LogInformation(
                "Niche search: no enabled (single-analyzer, content-discovery, discover-niche-content, " +
                "niche-search, {NetworkKey}) route and no actor_mappings_json entry for organization " +
                "{OrganizationId} — reporting Unconfigured (CD-13), not a failure.",
                request.NetworkKey,
                request.OrganizationId);

            return new NicheSearchResolution(SourceOutcome.Unconfigured, []);
        }

        var apiKey = await ResolveApiKeyAsync(request.OrganizationId, ct);
        if (apiKey is null)
        {
            // An actor is configured but Apify itself is not connected — that is a stage the operator
            // still has to finish setting up, so it is Unconfigured rather than a provider failure.
            return new NicheSearchResolution(SourceOutcome.Unconfigured, []);
        }

        foreach (var actorId in actorIds)
        {
            ct.ThrowIfCancellationRequested();

            var attempt = await DispatchAsync(actorId, apiKey, request, ct);
            if (attempt is null)
            {
                // Transport/HTTP failure for this actor — try the next configured one, if any.
                continue;
            }

            return attempt;
        }

        // Every configured actor failed transport/HTTP. The route IS configured, so this is a provider
        // failure, never Unconfigured.
        return new NicheSearchResolution(SourceOutcome.Failed, []);
    }

    private async Task<IReadOnlyList<string>> ResolveActorIdsAsync(NicheSearchRequest request, CancellationToken ct)
    {
        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                request.OrganizationId,
                WorkflowKey,
                AnalysisType,
                StepKey,
                CapabilityKey,
                request.NetworkKey),
            ct);

        if (route.IsSuccess)
        {
            var configured = route.Value
                .Where(candidate => candidate.IsEnabled && !string.IsNullOrWhiteSpace(candidate.ActorId))
                .OrderBy(candidate => candidate.Priority)
                .Select(candidate => candidate.ActorId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (configured.Count > 0)
            {
                return configured;
            }
        }

        // Fall back to the raw actor_mappings_json entry — still configuration, exactly
        // ApifyProfileScrapeResolver.cs:146-151 (design §8.1).
        var integrationResult = await _integrationResolver.ResolveProviderAsync(request.OrganizationId, ApifyProviderKey, ct);
        if (integrationResult.IsFailure
            || !integrationResult.Value.Credentials.TryGetValue("actor_mappings_json", out var mappingsJson)
            || string.IsNullOrWhiteSpace(mappingsJson))
        {
            return [];
        }

        return ApifyActorMappingLookup.GetAllMappedActors(mappingsJson, request.NetworkKey, CapabilityKey);
    }

    private async Task<string?> ResolveApiKeyAsync(Guid organizationId, CancellationToken ct)
    {
        var integrationResult = await _integrationResolver.ResolveProviderAsync(organizationId, ApifyProviderKey, ct);
        if (integrationResult.IsFailure)
        {
            return null;
        }

        if (integrationResult.Value.Credentials.TryGetValue("api_key", out var apiKey)
            && !string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey;
        }

        return integrationResult.Value.Credentials.TryGetValue("api_token", out var apiToken)
            && !string.IsNullOrWhiteSpace(apiToken)
                ? apiToken
                : null;
    }

    private async Task<NicheSearchResolution?> DispatchAsync(
        string actorId,
        string apiKey,
        NicheSearchRequest request,
        CancellationToken ct)
    {
        var resultsPerPage = Math.Clamp(request.MaxResults <= 0 ? 20 : request.MaxResults, 1, 100);
        var input = _adapter.BuildSearchQueriesInput(request.SearchTerm, resultsPerPage);
        var body = JsonSerializer.Serialize(input, JsonOptions);
        var url = $"https://api.apify.com/v2/acts/{Uri.EscapeDataString(actorId)}/run-sync-get-dataset-items?token={Uri.EscapeDataString(apiKey)}";

        using var http = _httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Niche search actor {ActorId} timed out.", actorId);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Niche search actor {ActorId} transport failure.", actorId);
            return null;
        }

        using var responseHandle = response;
        var payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Niche search actor {ActorId} HTTP error {StatusCode}.",
                actorId,
                (int)response.StatusCode);
            return null;
        }

        JsonElement items;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "[]" : payload);
            items = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            _logger.LogWarning("Niche search actor {ActorId} returned non-JSON payload.", actorId);
            return new NicheSearchResolution(SourceOutcome.Failed, []);
        }

        var (mapped, totalCount, skippedCount) = _adapter.MapItems(items);

        if (totalCount == 0)
        {
            // A legitimate empty response is a success: the search ran, nothing matched.
            return new NicheSearchResolution(SourceOutcome.Ran, []);
        }

        if (mapped.Count == 0)
        {
            // The configured actor answered but nothing in its response shape is mappable — a
            // configuration/actor-compatibility problem, not "unconfigured" (design §7.1).
            _logger.LogWarning(
                "Niche search actor {ActorId} returned {ItemCount} item(s) but zero mapped — the " +
                "actor's response shape does not match the documented TikTok item shape.",
                actorId,
                totalCount);
            return new NicheSearchResolution(SourceOutcome.Failed, []);
        }

        if (skippedCount > 0)
        {
            _logger.LogInformation(
                "Niche search actor {ActorId}: {SkippedCount} of {TotalCount} item(s) skipped (unmappable shape).",
                actorId,
                skippedCount,
                totalCount);
        }

        return new NicheSearchResolution(SourceOutcome.Ran, mapped.Take(request.MaxResults <= 0 ? mapped.Count : request.MaxResults).ToList());
    }

    /// <summary>
    /// Wraps a pre-resolved credential as a minimal <see cref="IIntegrationResolver"/> so the test
    /// constructor can reuse the same resolution path.
    /// </summary>
    private sealed class PreResolvedIntegrationResolver : IIntegrationResolver
    {
        private readonly ResolvedProviderCredential _credential;

        public PreResolvedIntegrationResolver(ResolvedProviderCredential credential)
        {
            _credential = credential;
        }

        public Task<Result<ResolvedIntegration>> ResolveProviderAsync(
            Guid organizationId,
            string providerKey,
            CancellationToken cancellationToken = default)
        {
            var integration = new ResolvedIntegration(
                organizationId,
                _credential.ProviderKey,
                ProviderDisplayName: "Apify (test)",
                Category: IntegrationProviderCategory.DataExtraction,
                ConnectionMode: _credential.ConnectionMode,
                Status: IntegrationStatus.Connected,
                IsReadOnly: false,
                SelectedAiModel: null,
                Credentials: _credential.Credentials);

            return Task.FromResult(Result.Success(integration));
        }
    }
}
