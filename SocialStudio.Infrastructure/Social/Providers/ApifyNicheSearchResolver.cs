using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Features.Social.ContentGeneration.Discovery;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using SocialStudio.Infrastructure.Social.Providers.Apify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace SocialStudio.Infrastructure.Social.Providers;

/// <summary>
/// Infrastructure implementation of <see cref="INicheSearchResolver"/>.
///
/// Actor resolution is delegated to <see cref="ApifyNicheSearchActorConfiguration"/>, which is the SAME
/// type the integrations checklist probes — so "configured" means one thing everywhere (design §8.1,
/// CD-12/CD-13, D12/D13: never a literal id, never a throw).
///
/// Only <c>tiktok</c> has a working adapter today (<see cref="TikTokNicheSearchAdapter"/>) — any other
/// network degrades to <see cref="SourceOutcome.Unconfigured"/> rather than crashing.
/// </summary>
public sealed class ApifyNicheSearchResolver : INicheSearchResolver
{
    private const string ApifyProviderKey = ApifyNicheSearchActorConfiguration.ApifyProviderKey;
    private const string HttpClientName = nameof(ApifyNicheSearchResolver);

    /// <summary>
    /// Payload previews are capped so a large dataset response cannot flood the log. Apify's
    /// validation errors are short and always land well inside this budget.
    /// </summary>
    private const int MaxLoggedPayloadChars = 2000;

    /// <summary>Budget used when the caller does not ask for a specific number of results.</summary>
    private const int DefaultMaxResults = 20;

    /// <summary>
    /// Hard per-input ceiling. The call is synchronous against a 120 s HttpClient timeout, so no single
    /// input may ask the actor for an unbounded scrape however generous the caller's budget is.
    /// </summary>
    private const int MaxResultsPerInput = 100;

    /// <summary>
    /// Floor for a split share, so a many-way split still asks for a useful page. It is itself capped by
    /// the total budget: a caller asking for 2 results must never make us request 5 per hashtag.
    /// </summary>
    private const int MinResultsPerInput = 5;

    /// <summary>
    /// How far past the requested budget a response may land before it is reported as an overrun.
    ///
    /// The actor's per-hashtag cap is a field of ITS schema, and we cannot know its name for an
    /// arbitrary operator-chosen actor — a real run asked for 5 and was billed for 202, which proves the
    /// field we send was ignored entirely. Nothing in the log said so. A little slack is normal (the
    /// budget is split across hashtags with a ceiling division, and actors round up to a page), so the
    /// warning fires only when the excess is material rather than arithmetic.
    /// </summary>
    private const double OverrunWarningFactor = 1.5;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Divides the caller's TOTAL result budget across the derived inputs that travel in one call.
    ///
    /// The actor applies <c>resultsPerPage</c> per hashtag, so sending the full budget with 3 hashtags
    /// asks for three times what was requested — the run then scrapes far more than intended, bills for
    /// it, and (as happened in production) exceeds the 120 s synchronous timeout.
    ///
    /// Arithmetic: ceiling division, so the shares never under-serve the budget — 72 requested across
    /// 3 hashtags → 24 each (3 × 24 = 72); 70 across 3 → 24 each (72 ≥ 70); 72 with 1 hashtag → 72.
    /// The share is then floored (never 0 for a tiny budget) and capped at <see cref="MaxResultsPerInput"/>.
    /// The caller's <c>MaxResults</c> stays the upper bound of the whole call: the mapped candidates are
    /// truncated to it before returning.
    /// </summary>
    private static int SplitBudgetPerInput(int totalBudget, int inputCount)
    {
        if (inputCount <= 1)
        {
            return Math.Clamp(totalBudget, 1, MaxResultsPerInput);
        }

        var share = (totalBudget + inputCount - 1) / inputCount;
        share = Math.Max(share, Math.Min(MinResultsPerInput, totalBudget));

        return Math.Clamp(share, 1, MaxResultsPerInput);
    }

    private static string Truncate(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxChars ? value : $"{value[..maxChars]}…[truncated]";
    }

    /// <summary>
    /// Renders provider-reported errors as <c>errorCode:input</c> pairs — the two facts an operator
    /// needs to act (what went wrong, and for which term).
    /// </summary>
    private static string DescribeProviderErrors(IReadOnlyList<NicheSearchProviderError> errors) =>
        string.Join(
            ", ",
            errors.Select(e => $"{e.ErrorCode ?? "UNKNOWN_ERROR"}:{e.Input ?? "(unnamed input)"}"));

    private readonly ApifyNicheSearchActorConfiguration _actorConfiguration;
    private readonly IIntegrationResolver _integrationResolver;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ApifyNicheSearchResolver> _logger;
    private readonly ApifyNicheSearchGuardOptions _guards;
    private readonly TikTokNicheSearchAdapter _adapter = new();

    /// <summary>Production constructor — resolves credentials per request via <see cref="IIntegrationResolver"/>.</summary>
    public ApifyNicheSearchResolver(
        IWorkflowConfigurationResolver workflowResolver,
        IIntegrationResolver integrationResolver,
        IHttpClientFactory httpClientFactory,
        ILogger<ApifyNicheSearchResolver>? logger = null,
        IOptions<ApifyNicheSearchGuardOptions>? guardOptions = null)
    {
        ArgumentNullException.ThrowIfNull(workflowResolver);
        ArgumentNullException.ThrowIfNull(integrationResolver);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _actorConfiguration = new ApifyNicheSearchActorConfiguration(workflowResolver, integrationResolver);
        _integrationResolver = integrationResolver;
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<ApifyNicheSearchResolver>.Instance;

        // Absent configuration must never mean "unguarded": the defaults ARE the safety net, and they
        // are the values that apply in the environment where a runaway run already cost real money.
        _guards = guardOptions?.Value ?? new ApifyNicheSearchGuardOptions();
    }

    /// <summary>Test constructor — accepts a pre-resolved credential to avoid I/O in unit tests.</summary>
    internal ApifyNicheSearchResolver(
        IWorkflowConfigurationResolver workflowResolver,
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        ILogger<ApifyNicheSearchResolver>? logger = null,
        IOptions<ApifyNicheSearchGuardOptions>? guardOptions = null)
        : this(
            workflowResolver,
            new PreResolvedIntegrationResolver(credential),
            httpClientFactory,
            logger,
            guardOptions)
    {
    }

    public async Task<NicheSearchResolution> SearchAsync(NicheSearchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ApifyNicheSearchActorConfiguration.HasAdapter(request.NetworkKey))
        {
            // No adapter exists for this network yet (design §8.1 network-widening note) — a network
            // configured without a working adapter degrades through Unconfigured, never a crash.
            return new NicheSearchResolution(SourceOutcome.Unconfigured, []);
        }

        var actorIds = await _actorConfiguration.ResolveActorIdsAsync(request.OrganizationId, request.NetworkKey, ct);
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

        // Curated hashtags from the niche catalog win outright — they are the intentional key, already
        // normalized by the domain entity. Only when the caller has none (a client with no catalog niche
        // assigned) do we fall back to DERIVING them from the free-text phrase, because a niche phrase
        // is not a hashtag. When nothing survives that derivation there is no tag worth asking for, and
        // calling a paid actor to be told the tag does not exist helps nobody.
        var hashtags = request.Hashtags is { Count: > 0 } curated
            ? curated
            : NicheHashtagDerivation.Derive(request.SearchTerm);
        if (hashtags.Count == 0)
        {
            _logger.LogWarning(
                "Niche search: the search term {SearchTerm} yields no searchable hashtag (it is blank, " +
                "punctuation only, or nothing but connector words), so no actor was called. " +
                "OrganizationId={OrganizationId} NetworkKey={NetworkKey}",
                request.SearchTerm,
                request.OrganizationId,
                request.NetworkKey);

            return new NicheSearchResolution(SourceOutcome.Failed, []);
        }

        foreach (var actorId in actorIds)
        {
            ct.ThrowIfCancellationRequested();

            var attempt = await DispatchAsync(actorId, apiKey, hashtags, request, ct);
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

    /// <summary>
    /// Runs one configured actor, trying the documented input shapes in order.
    ///
    /// WHY A RESHAPE RETRY: the input schema is a property of the ACTOR the operator chose, and we
    /// cannot know it in advance — the store has both hashtag-style Discover actors
    /// (<c>{ "hashtags": [...] }</c>) and keyword-search scrapers (<c>{ "searchQueries": [...] }</c>).
    /// The hashtag shape goes first because that is what the Discover-style actors operators actually
    /// map document. One bounded reshape retry costs one extra call and removes a whole class of
    /// dead-end misconfiguration where the actor IS correct and only the body was wrong.
    ///
    /// ONLY on 400. A 400 is the actor rejecting the SHAPE; 401/403 (credentials), 429 (quota) and 5xx
    /// (provider outage) say nothing about the body, so resending it would just double the damage.
    /// </summary>
    private async Task<NicheSearchResolution?> DispatchAsync(
        string actorId,
        string apiKey,
        IReadOnlyList<string> hashtags,
        NicheSearchRequest request,
        CancellationToken ct)
    {
        var totalBudget = Math.Clamp(request.MaxResults <= 0 ? DefaultMaxResults : request.MaxResults, 1, MaxResultsPerInput);

        // The hashtag shape carries an ARRAY, and the actor applies resultsPerPage to EACH element — so
        // passing the caller's whole budget multiplies the scrape by the number of derived hashtags.
        // The budget is a TOTAL, so it is divided across them.
        (string ShapeName, object Input)[] shapes =
        [
            ("hashtags", _adapter.BuildHashtagsInput(hashtags, SplitBudgetPerInput(totalBudget, hashtags.Count))),
            // One query, one budget: the searchQueries shape sends a single term, so nothing multiplies.
            ("searchQueries", _adapter.BuildSearchQueriesInput(request.SearchTerm, totalBudget)),
        ];

        for (var index = 0; index < shapes.Length; index++)
        {
            ct.ThrowIfCancellationRequested();

            var (shapeName, input) = shapes[index];
            var isLastShape = index == shapes.Length - 1;

            var attempt = await DispatchInputShapeAsync(actorId, apiKey, request, shapeName, input, totalBudget, ct);

            if (attempt.ShapeRejected && !isLastShape)
            {
                _logger.LogWarning(
                    "Niche search actor {ActorId} rejected the {ShapeName} input shape with HTTP 400 — " +
                    "retrying the SAME actor once with the {NextShapeName} shape.",
                    actorId,
                    shapeName,
                    shapes[index + 1].ShapeName);
                continue;
            }

            return attempt.Resolution;
        }

        return null;
    }

    private async Task<(NicheSearchResolution? Resolution, bool ShapeRejected)> DispatchInputShapeAsync(
        string actorId,
        string apiKey,
        NicheSearchRequest request,
        string shapeName,
        object input,
        int totalBudget,
        CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(input, JsonOptions);

        // PLATFORM guards, as query parameters of the RUN ENDPOINT — not actor input. They bound cost
        // and duration whatever the operator's actor does with the body, which is the only protection
        // available while the actor's own per-hashtag limit field name is unknown to us.
        var guardQuery = _guards.ToQueryString();

        var runPath = $"https://api.apify.com/v2/acts/{Uri.EscapeDataString(actorId)}/run-sync-get-dataset-items";
        var url = $"{runPath}?token={Uri.EscapeDataString(apiKey)}{guardQuery}";

        // The request body is the only evidence that explains a 4xx: the actor rejected THIS shape.
        // Mirrors ApifySocialContentExtractor's START line, token masked the same way.
        _logger.LogInformation(
            "Niche search START. OrganizationId={OrganizationId} NetworkKey={NetworkKey} ActorId={ActorId} " +
            "InputShape={ShapeName} SearchTerm={SearchTerm} Url={Url} RequestBytes={RequestBytes} " +
            "RequestPreview={RequestPreview}",
            request.OrganizationId,
            request.NetworkKey,
            actorId,
            shapeName,
            request.SearchTerm,
            $"{runPath}?token=***{guardQuery}",
            Encoding.UTF8.GetByteCount(body),
            Truncate(body, MaxLoggedPayloadChars));

        using var http = _httpClientFactory.CreateClient(HttpClientName);
        var stopwatch = Stopwatch.StartNew();

        var exchange = await SendAndReadAsync(http, url, body, actorId, shapeName, stopwatch, ct);
        if (exchange is null)
        {
            // Every terminal outcome of the exchange has already been logged with its elapsed time.
            return (null, false);
        }

        var (response, payload) = exchange.Value;
        using var responseHandle = response;
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            // Apify puts the reason in the body ("Input is not valid: Field input.X is required" and
            // friends). Logging only the status code discards the one thing that identifies which
            // field the configured actor disagrees with.
            _logger.LogWarning(
                "Niche search RESPONSE ERROR after {ElapsedMs} ms. Actor {ActorId} answered HTTP " +
                "{StatusCode} for the {ShapeName} input shape. " +
                "RequestPreview={RequestPreview} ResponsePreview={ResponsePreview}",
                stopwatch.ElapsedMilliseconds,
                actorId,
                (int)response.StatusCode,
                shapeName,
                Truncate(body, MaxLoggedPayloadChars),
                Truncate(payload, MaxLoggedPayloadChars));

            // Only 400 means "this actor does not accept THIS body" — the one failure a different
            // input shape can fix. Auth, quota and outage failures are never reshaped.
            var shapeRejected = response.StatusCode == HttpStatusCode.BadRequest;
            return (null, shapeRejected);
        }

        JsonElement items;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "[]" : payload);
            items = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Niche search NON-JSON RESPONSE after {ElapsedMs} ms. ActorId={ActorId} InputShape={ShapeName} " +
                "ResponseBytes={ResponseBytes} ResponsePreview={ResponsePreview}",
                stopwatch.ElapsedMilliseconds,
                actorId,
                shapeName,
                Encoding.UTF8.GetByteCount(payload),
                Truncate(payload, MaxLoggedPayloadChars));

            return (new NicheSearchResolution(SourceOutcome.Failed, []), false);
        }

        var returnedItemCount = items.ValueKind == JsonValueKind.Array ? items.GetArrayLength() : 0;

        // Mirrors ApifySocialContentExtractor's SUCCESS line: a run that WORKS must leave a trace too,
        // otherwise the START line is the only evidence and a success cannot be told from a hang.
        _logger.LogInformation(
            "Niche search SUCCESS after {ElapsedMs} ms. ActorId={ActorId} InputShape={ShapeName} " +
            "StatusCode={StatusCode} ResponseBytes={ResponseBytes} ItemCount={ItemCount}",
            stopwatch.ElapsedMilliseconds,
            actorId,
            shapeName,
            (int)response.StatusCode,
            Encoding.UTF8.GetByteCount(payload),
            returnedItemCount);

        if (returnedItemCount > totalBudget * OverrunWarningFactor)
        {
            // THE signal that the actor ignored the per-page field we sent. Dashboard-saved actor input
            // does not apply to API runs, and a field name we guessed wrong is silently discarded — so
            // "we asked for 5 and were served 202" is the only observable evidence, and until now
            // nothing recorded it. The platform guards on the run URL are what keep that affordable.
            _logger.LogWarning(
                "Niche search RETURNED MORE ITEMS THAN REQUESTED. ActorId={ActorId} InputShape={ShapeName} " +
                "ItemCount={ItemCount} RequestedBudget={RequestedBudget}. The actor ignored the per-page " +
                "limit field we send — its real input field name is a property of THAT actor and may need " +
                "a per-actor mapping. Cost is still bounded by the run URL's platform guards.",
                actorId,
                shapeName,
                returnedItemCount,
                totalBudget);
        }

        var (mapped, totalCount, skippedCount, providerErrors) = _adapter.MapItems(items);

        if (totalCount == 0)
        {
            // A legitimate empty response is a success: the search ran, nothing matched.
            return (new NicheSearchResolution(SourceOutcome.Ran, []), false);
        }

        if (mapped.Count == 0 && providerErrors.Count > 0)
        {
            // The actor ANSWERED — with "that hashtag does not exist". Nothing was found, so the run
            // still fails, but the diagnosis must name the dead term instead of blaming the response
            // shape: the shape is fine and the mapping needs no correction whatsoever.
            _logger.LogWarning(
                "Niche search actor {ActorId} found no hashtag for the search term {SearchTerm}: the " +
                "provider reported {ErrorCount} not-found term(s) — {ProviderErrors}. The response shape " +
                "is fine; the hashtag simply does not exist on the network. InputShape={ShapeName}",
                actorId,
                request.SearchTerm,
                providerErrors.Count,
                DescribeProviderErrors(providerErrors),
                shapeName);

            return (new NicheSearchResolution(SourceOutcome.Failed, []), false);
        }

        if (providerErrors.Count > 0)
        {
            // Several hashtags travel in one call; one of them being dead cannot void the others.
            _logger.LogInformation(
                "Niche search actor {ActorId}: {ErrorCount} of the searched term(s) do not exist " +
                "({ProviderErrors}) — the run continues with the {MappedCount} candidate(s) that did resolve.",
                actorId,
                providerErrors.Count,
                DescribeProviderErrors(providerErrors),
                mapped.Count);
        }

        if (mapped.Count == 0)
        {
            // The configured actor answered but nothing in its response shape is mappable — a
            // configuration/actor-compatibility problem, not "unconfigured" (design §7.1).
            //
            // The response preview is the ONLY way to learn what the operator's actor actually returns
            // (e.g. whether a Discover-style actor reports per-video metrics at all). Without it this
            // warning says "it did not match" and nothing about what to correct in MapItem.
            _logger.LogWarning(
                "Niche search actor {ActorId} returned {ItemCount} item(s) but zero mapped — the " +
                "actor's response shape does not match the documented TikTok item shape. " +
                "InputShape={ShapeName} ResponsePreview={ResponsePreview}",
                actorId,
                totalCount,
                shapeName,
                Truncate(payload, MaxLoggedPayloadChars));
            return (new NicheSearchResolution(SourceOutcome.Failed, []), false);
        }

        if (skippedCount > 0)
        {
            _logger.LogInformation(
                "Niche search actor {ActorId}: {SkippedCount} of {TotalCount} item(s) skipped (unmappable shape).",
                actorId,
                skippedCount,
                totalCount);
        }

        return (
            new NicheSearchResolution(
                SourceOutcome.Ran,
                mapped.Take(request.MaxResults <= 0 ? mapped.Count : request.MaxResults).ToList()),
            false);
    }

    /// <summary>
    /// Sends the actor call AND reads its body under one protective structure, so no failure after the
    /// START line can be silent.
    ///
    /// The response read used to sit outside every try/catch: a transport or timeout failure mid-read
    /// propagated unlogged and surfaced to the operator as a bare HTTP 500 with a log containing only
    /// the START line. Reading the body is part of the same exchange and fails the same ways, so it is
    /// protected the same way.
    ///
    /// Returns <c>null</c> when the exchange failed (already logged, with its elapsed time). A CALLER
    /// cancellation is logged and RETHROWN: swallowing a genuine abort would report a failure nobody is
    /// waiting for, but it must not be invisible either.
    /// </summary>
    private async Task<(HttpResponseMessage Response, string Payload)?> SendAndReadAsync(
        HttpClient http,
        string url,
        string body,
        string actorId,
        string shapeName,
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct);
            var payload = await response.Content.ReadAsStringAsync(ct);
            return (response, payload);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            response?.Dispose();
            stopwatch.Stop();

            _logger.LogWarning(
                ex,
                "Niche search TIMED OUT after {ElapsedMs} ms. ActorId={ActorId} InputShape={ShapeName}. " +
                "The HttpClient's per-request timeout (120s for this client) fired — the actor was still " +
                "running when we stopped waiting.",
                stopwatch.ElapsedMilliseconds,
                actorId,
                shapeName);

            return null;
        }
        catch (OperationCanceledException ex)
        {
            response?.Dispose();
            stopwatch.Stop();

            _logger.LogWarning(
                ex,
                "Niche search CANCELLED BY THE CALLER after {ElapsedMs} ms (browser or proxy aborted the " +
                "request). ActorId={ActorId} InputShape={ShapeName}. The cancellation is rethrown — the " +
                "actor run itself is NOT stopped and may still be billed.",
                stopwatch.ElapsedMilliseconds,
                actorId,
                shapeName);

            throw;
        }
        catch (HttpRequestException ex)
        {
            response?.Dispose();
            stopwatch.Stop();

            _logger.LogWarning(
                ex,
                "Niche search TRANSPORT FAILURE after {ElapsedMs} ms (could not reach the provider). " +
                "ActorId={ActorId} InputShape={ShapeName} ExceptionType={ExceptionType} Message={Message} " +
                "InnerType={InnerType} InnerMessage={InnerMessage}",
                stopwatch.ElapsedMilliseconds,
                actorId,
                shapeName,
                ex.GetType().FullName,
                ex.Message,
                ex.InnerException?.GetType().FullName,
                ex.InnerException?.Message);

            return null;
        }
        catch (Exception ex)
        {
            // Deliberately broad and deliberately LAST: anything unforeseen here used to escape as an
            // unlogged HTTP 500. The run still degrades to Failed like any other provider failure.
            response?.Dispose();
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Niche search UNEXPECTED FAILURE after {ElapsedMs} ms. ActorId={ActorId} " +
                "InputShape={ShapeName} ExceptionType={ExceptionType} Message={Message}",
                stopwatch.ElapsedMilliseconds,
                actorId,
                shapeName,
                ex.GetType().FullName,
                ex.Message);

            return null;
        }
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
