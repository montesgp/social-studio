using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Infrastructure.Integrations;
using Incoders.Hive.Infrastructure.Social.Providers.Apify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Infrastructure.Social.Providers;

/// <summary>
/// Infrastructure implementation of <see cref="IProfileScrapeResolver"/>.
/// Resolves all actor ids for (networkKey="instagram", variantKey="profile") using the
/// encrypted <c>actor_mappings_json</c> credential. The primary actor (IsPrimary == true)
/// is placed first; remaining actors follow in JSON array order as backups.
///
/// FALLBACK BEHAVIOUR (PA-05, Decision 3):
///   1. Run primary actor (IsPrimary == true, or first in array if none marked primary).
///   2. On failure (transport / HTTP error / empty dataset / no profile-result item in array):
///      record a ProviderFailureDiagnostic, retry with each backup actor in order.
///   3. All exhausted → Result.Failure with diagnostics.
///   4. Backup success → IsPartial = true (PA-08 precedent: partial data path).
///
/// MUNDO 1 ISOLATION:
///   This resolver DOES NOT route through ApifySocialContentExtractor or ISocialPlatformScrapeAdapter.
///   It never touches the single-video pipeline (PA-REG-01, Design Decision 2).
///
/// OPERATOR SETUP (INT-01, INT-02):
///   actor_mappings_json must contain Instagram actors tagged variantKey="profile".
///   The startup backfill (ApifyActorMappingBackfill) re-tags legacy "default" entries
///   to "profile"/"social-extraction" at startup. After backfill, the UI-persisted JSON
///   always has explicit variant keys.
///   Missing "profile" entries → 422 Integration.ProfileActorNotConfigured.
/// </summary>
public sealed class ApifyProfileScrapeResolver : IProfileScrapeResolver
{
    private const string CapabilityKey = "profile";
    private const string NetworkKey = "instagram";
    private const string HttpClientName = nameof(ApifyProfileScrapeResolver);
    private const string ApifyProviderKey = "apify";

    public static readonly Error ApifyNotConfigured = Error.Validation(
        "integration.apify_not_configured",
        "integration.errors.apify_not_configured");

    public static readonly Error ProfileActorNotConfigured = Error.Validation(
        "integration.profile_actor_not_configured",
        "integration.errors.profile_actor_not_configured");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IIntegrationResolver _integrationResolver;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ApifyProfileScrapeResolver> _logger;

    /// <summary>
    /// Production constructor — resolves credentials per request via <see cref="IIntegrationResolver"/>.
    /// </summary>
    public ApifyProfileScrapeResolver(
        IIntegrationResolver integrationResolver,
        IHttpClientFactory httpClientFactory,
        ILogger<ApifyProfileScrapeResolver>? logger = null)
    {
        _integrationResolver = integrationResolver;
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<ApifyProfileScrapeResolver>.Instance;
    }

    /// <summary>
    /// Test constructor — accepts a pre-resolved credential to avoid I/O in unit tests.
    /// </summary>
    internal ApifyProfileScrapeResolver(
        ResolvedProviderCredential credential,
        IHttpClientFactory httpClientFactory,
        ILogger<ApifyProfileScrapeResolver>? logger = null)
    {
        _integrationResolver = new PreResolvedIntegrationResolver(credential);
        _httpClientFactory = httpClientFactory;
        _logger = logger ?? NullLogger<ApifyProfileScrapeResolver>.Instance;
    }

    public async Task<Result<ProfileScrapeResult>> ResolveAsync(
        ProfileScrapeRequest request,
        CancellationToken ct = default)
    {
        // ── Credential resolution ─────────────────────────────────────────────
        var integrationResult = await _integrationResolver.ResolveProviderAsync(
            request.OrganizationId,
            ApifyProviderKey,
            ct);

        if (integrationResult.IsFailure)
        {
            _logger.LogWarning(
                "Apify profile scrape cannot start — Apify integration not configured. " +
                "OrganizationId={OrganizationId} AnalysisId={AnalysisId} Error={Error}",
                request.OrganizationId,
                request.AnalysisId,
                integrationResult.Error.Code);

            return Result.Failure<ProfileScrapeResult>(ApifyNotConfigured);
        }

        var integration = integrationResult.Value;

        if (!integration.Credentials.TryGetValue("api_key", out var apiKey)
            && !integration.Credentials.TryGetValue("api_token", out apiKey)
            || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "Apify profile scrape cannot start — api_key credential missing. " +
                "OrganizationId={OrganizationId} AnalysisId={AnalysisId}",
                request.OrganizationId,
                request.AnalysisId);

            return Result.Failure<ProfileScrapeResult>(ApifyNotConfigured);
        }

        if (!integration.Credentials.TryGetValue("actor_mappings_json", out var mappingsJson)
            || string.IsNullOrWhiteSpace(mappingsJson))
        {
            _logger.LogWarning(
                "Apify profile scrape cannot start — actor_mappings_json credential missing. " +
                "OrganizationId={OrganizationId} AnalysisId={AnalysisId}",
                request.OrganizationId,
                request.AnalysisId);

            return Result.Failure<ProfileScrapeResult>(ProfileActorNotConfigured);
        }

        return await ResolveWithCredentialsAsync(apiKey, mappingsJson, request, ct);
    }

    private async Task<Result<ProfileScrapeResult>> ResolveWithCredentialsAsync(
        string apiKey,
        string mappingsJson,
        ProfileScrapeRequest request,
        CancellationToken ct)
    {
        // ── Actor resolution ──────────────────────────────────────────────────
        var actorIds = request.PreferredActorIds?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? GetAllMappedActors(mappingsJson, NetworkKey, CapabilityKey);
        if (actorIds.Count == 0)
        {
            _logger.LogWarning(
                "No actor_mappings_json entry found for (networkKey={NetworkKey}, variantKey={CapabilityKey}). " +
                "OrganizationId={OrganizationId} AnalysisId={AnalysisId}",
                NetworkKey,
                CapabilityKey,
                request.OrganizationId,
                request.AnalysisId);

            return Result.Failure<ProfileScrapeResult>(ProfileActorNotConfigured);
        }

        _logger.LogInformation(
            "Apify profile scrape actor resolution: {ActorCount} actor(s) configured. " +
            "DefaultActorId={DefaultActorId} BackupActorId={BackupActorId} " +
            "OrganizationId={OrganizationId} AnalysisId={AnalysisId}",
            actorIds.Count,
            actorIds[0],
            actorIds.Count >= 2 ? actorIds[1] : "(none)",
            request.OrganizationId,
            request.AnalysisId);

        // ── Username ──────────────────────────────────────────────────────────
        var username = InstagramProfileApifyScrapeAdapter.ParseUsername(request.ProfileUrlOrHandle);

        // ── Adapter ───────────────────────────────────────────────────────────
        var adapter = new InstagramProfileApifyScrapeAdapter(
            _logger as ILogger<InstagramProfileApifyScrapeAdapter>
            ?? NullLogger<InstagramProfileApifyScrapeAdapter>.Instance);

        // ── Attempt default → fallback to backup ──────────────────────────────
        var diagnostics = new List<ProviderFailureDiagnostic>();

        for (var i = 0; i < actorIds.Count; i++)
        {
            var actorId = actorIds[i];
            var isFallback = i > 0;

            _logger.LogInformation(
                "Apify profile scrape DISPATCH: attempt {AttemptIndex} actorId={ActorId} fallback={IsFallback} " +
                "username={Username} OrganizationId={OrganizationId} AnalysisId={AnalysisId}",
                i + 1,
                actorId,
                isFallback,
                username,
                request.OrganizationId,
                request.AnalysisId);

            var attemptResult = await AttemptScrapeAsync(
                actorId, apiKey, username, adapter, request, i + 1, ct);

            if (attemptResult.IsSuccess)
            {
                _logger.LogInformation(
                    "Apify profile scrape SUCCESS: actorId={ActorId} fallback={IsFallback} " +
                    "username={Username} PostsReceived={PostsReceived} " +
                    "OrganizationId={OrganizationId} AnalysisId={AnalysisId}",
                    actorId,
                    isFallback,
                    username,
                    attemptResult.Value.AllPosts.Count,
                    request.OrganizationId,
                    request.AnalysisId);

                return BuildResult(attemptResult.Value, diagnostics, isFallback, actorId);
            }

            // Record diagnostic for this failed attempt
            var diagnostic = CreateDiagnostic(actorId, i + 1, attemptResult.Error);
            if (diagnostic.IsSuccess)
            {
                diagnostics.Add(diagnostic.Value);
            }

            _logger.LogWarning(
                "Apify profile scrape ATTEMPT FAILED: actorId={ActorId} attempt={Attempt} " +
                "errorCode={ErrorCode} OrganizationId={OrganizationId} AnalysisId={AnalysisId}",
                actorId,
                i + 1,
                attemptResult.Error.Code,
                request.OrganizationId,
                request.AnalysisId);

            // Stop if no more actors available
            if (i + 1 >= actorIds.Count)
            {
                break;
            }
        }

        // All actors exhausted
        _logger.LogError(
            "Apify profile scrape EXHAUSTED all {ActorCount} actor(s). All attempts failed. " +
            "OrganizationId={OrganizationId} AnalysisId={AnalysisId}",
            actorIds.Count,
            request.OrganizationId,
            request.AnalysisId);

        var finalError = diagnostics.Count > 0
            ? Error.Failure(diagnostics[^1].FailureCode, diagnostics[^1].MessageKey)
            : IntegrationErrors.ExternalProviderUnavailable;

        return Result.Failure<ProfileScrapeResult>(finalError);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<Result<ProfileMappingResult>> AttemptScrapeAsync(
        string actorId,
        string apiKey,
        string username,
        InstagramProfileApifyScrapeAdapter adapter,
        ProfileScrapeRequest request,
        int attemptNumber,
        CancellationToken ct)
    {
        var input = adapter.BuildProfileActorInput(actorId, username, maxPostsPerProfile: 12);
        var body = JsonSerializer.Serialize(input, JsonOptions);
        var url = $"https://api.apify.com/v2/acts/{Uri.EscapeDataString(actorId)}/run-sync-get-dataset-items?token={Uri.EscapeDataString(apiKey)}";

        using var http = _httpClientFactory.CreateClient(HttpClientName);
        var stopwatch = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(
                url,
                new StringContent(body, Encoding.UTF8, "application/json"),
                ct);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Apify profile scrape TIMED OUT after {ElapsedMs} ms. " +
                "actorId={ActorId} attempt={Attempt} OrganizationId={OrganizationId}",
                stopwatch.ElapsedMilliseconds,
                actorId,
                attemptNumber,
                request.OrganizationId);

            return Result.Failure<ProfileMappingResult>(IntegrationErrors.ProviderTimeout);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                ex,
                "Apify profile scrape TRANSPORT FAILURE after {ElapsedMs} ms. " +
                "actorId={ActorId} attempt={Attempt} OrganizationId={OrganizationId}",
                stopwatch.ElapsedMilliseconds,
                actorId,
                attemptNumber,
                request.OrganizationId);

            return Result.Failure<ProfileMappingResult>(IntegrationErrors.ExternalProviderUnavailable);
        }

        using var responseHandle = response;
        var payload = await response.Content.ReadAsStringAsync(ct);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Apify profile scrape HTTP ERROR after {ElapsedMs} ms. " +
                "actorId={ActorId} attempt={Attempt} StatusCode={StatusCode} " +
                "OrganizationId={OrganizationId}",
                stopwatch.ElapsedMilliseconds,
                actorId,
                attemptNumber,
                (int)response.StatusCode,
                request.OrganizationId);

            return Result.Failure<ProfileMappingResult>(MapHttpStatus(response));
        }

        // PA-06: select resultType == "profile" — returns null if not found
        var mapped = adapter.MapProfileResponse(payload);
        if (mapped is null)
        {
            _logger.LogWarning(
                "Apify profile scrape: no item with resultType=='profile' found in response. " +
                "actorId={ActorId} attempt={Attempt} PayloadLength={PayloadLength} " +
                "OrganizationId={OrganizationId}",
                actorId,
                attemptNumber,
                payload.Length,
                request.OrganizationId);

            return Result.Failure<ProfileMappingResult>(
                Error.Failure(
                    "integration.profile_result_not_found",
                    "integration.errors.profile_result_not_found"));
        }

        return Result.Success(mapped);
    }

    private static Result<ProfileScrapeResult> BuildResult(
        ProfileMappingResult mapped,
        IReadOnlyList<ProviderFailureDiagnostic> diagnostics,
        bool isFallback,
        string usedActorId)
    {
        // The owner filter (ownerId == instagramId) is applied downstream in ProfileMetricCalculator.
        // We return ALL posts raw so the calculator can apply the filter (PA-07).
        var providerAggregates = new ProfileAggregates(
            MediaCount: mapped.Profile.PostsCount,
            FollowersCount: mapped.Profile.FollowersCount,
            FollowingCount: mapped.Aggregates.FollowingCount,
            PostsReceived: mapped.AllPosts.Count,
            PostsProcessed: mapped.AllPosts.Count, // caller updates after own-filter+degrade
            DegradedPostCount: mapped.AllPosts.Count(p => p.IsDegraded));

        return Result.Success(new ProfileScrapeResult(
            Profile: mapped.Profile,
            OwnerPosts: mapped.AllPosts, // unfiltered — calculator applies owner-filter
            Reels: mapped.Reels,
            ProviderAggregates: providerAggregates,
            IsPartial: isFallback || mapped.AllPosts.Any(p => p.IsDegraded),
            Diagnostics: diagnostics,
            UsedActorId: usedActorId,
            FallbackUsed: isFallback));
    }

    /// <summary>
    /// Returns ALL actor ids whose (networkKey, variantKey) pair matches the request,
    /// with the primary actor (IsPrimary == true) returned first, followed by the remaining
    /// matches in JSON array order (backups).
    ///
    /// There is NO "default" fallback. Entries with variantKey == "default" are NOT used
    /// for profile resolution. The startup backfill (ApifyActorMappingBackfill) is responsible
    /// for re-tagging "default" entries to "profile" and "social-extraction" before this method
    /// is called in production. If no "profile" entries exist → returns empty → caller returns
    /// ProfileActorNotConfigured (clear 4xx).
    ///
    /// Tolerates JSON entries that are missing the isPrimary field (defaults to false).
    /// </summary>
    internal static IReadOnlyList<string> GetAllMappedActors(
        string actorMappingsJson,
        string networkKey,
        string variantKey)
    {
        try
        {
            var mappings = JsonSerializer.Deserialize<List<ApifyActorMappingEntry>>(
                actorMappingsJson, JsonOptions) ?? [];

            var matches = mappings
                .Where(m =>
                    !string.IsNullOrWhiteSpace(m.ActorId)
                    && Matches(m.NetworkKey, networkKey)
                    && Matches(m.VariantKey, variantKey))
                .ToList();

            if (matches.Count == 0)
            {
                return [];
            }

            // Primary first, then the remaining in original array order.
            // OrderBy is stable: non-primary entries preserve their relative JSON order.
            return matches
                .OrderByDescending(m => m.IsPrimary)
                .Select(m => m.ActorId.Trim())
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Result<ProviderFailureDiagnostic> CreateDiagnostic(
        string actorId,
        int attemptNumber,
        Error error) =>
        ProviderFailureDiagnostic.Create(
            CapabilityKey,
            actorId,
            attemptNumber,
            error.Code,
            error.MessageKey,
            isRetryable: false,
            fallbackAllowed: true,
            elapsedMs: 0,
            DateTime.UtcNow);

    private static Error MapHttpStatus(HttpResponseMessage response) =>
        (int)response.StatusCode switch
        {
            401 or 403 => IntegrationErrors.ProviderCredentialUnavailable,
            408 => IntegrationErrors.ProviderTimeout,
            429 => IntegrationErrors.ProviderRateLimited,
            >= 500 => IntegrationErrors.ExternalProviderUnavailable,
            _ => IntegrationErrors.ExternalProviderUnavailable,
        };

    private static bool Matches(string? value, string expected) =>
        string.Equals(value?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record ApifyActorMappingEntry(
        string NetworkKey,
        string VariantKey,
        string ActorId,
        bool IsPrimary = false);

    // ── Test helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps a pre-resolved credential as a minimal <see cref="IIntegrationResolver"/>
    /// so the test constructor can reuse the same resolution path.
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
                Category: Domain.Integrations.IntegrationProviderCategory.DataExtraction,
                ConnectionMode: _credential.ConnectionMode,
                Status: Domain.Integrations.IntegrationStatus.Connected,
                IsReadOnly: false,
                SelectedAiModel: null,
                Credentials: _credential.Credentials);

            return Task.FromResult(Result.Success(integration));
        }
    }
}
