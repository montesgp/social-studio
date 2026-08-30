using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// Per-stage configuration resolution for the YouTube Data API v3 origin (design §5.2), mirroring
/// <c>ApifyNicheSearchActorConfiguration</c>: an enabled workflow-step route wins first; otherwise
/// <c>Unconfigured</c> — never a literal endpoint or key, never a throw. The raw
/// <c>actor_mappings_json</c> fallback is Apify-only and is not exercised here (Apify origin is out of
/// scope for WU-5). A blank <see cref="NicheDiscoveryOriginOptions.BaseAddress"/> short-circuits to
/// <c>Unconfigured</c> before any resolution call is made.
/// </summary>
internal sealed class NicheDiscoveryOriginConfiguration
{
    internal const string YouTubeDataApiProviderKey = "youtube-data-api-v3";
    internal const string WorkflowKey = "niche-discovery";
    internal const string AnalysisType = "market-opportunity";
    internal const string ApiKeyCredentialKey = "api_key";

    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly IIntegrationResolver _integrationResolver;
    private readonly IOptions<NicheDiscoveryOriginOptions> _options;

    internal NicheDiscoveryOriginConfiguration(
        IWorkflowConfigurationResolver workflowResolver,
        IIntegrationResolver integrationResolver,
        IOptions<NicheDiscoveryOriginOptions> options)
    {
        ArgumentNullException.ThrowIfNull(workflowResolver);
        ArgumentNullException.ThrowIfNull(integrationResolver);
        ArgumentNullException.ThrowIfNull(options);

        _workflowResolver = workflowResolver;
        _integrationResolver = integrationResolver;
        _options = options;
    }

    internal static string StepKey(DiscoveryStage stage) => stage switch
    {
        DiscoveryStage.ChannelSearch => "channel-search",
        DiscoveryStage.VideoSearch => "video-search",
        DiscoveryStage.ChannelDetail => "channel-detail",
        DiscoveryStage.VideoDetail => "video-detail",
        DiscoveryStage.TrendingSweep => "trending-sweep",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown discovery stage."),
    };

    internal async Task<NicheDiscoveryOriginRoute> ResolveAsync(
        Guid organizationId,
        DiscoveryStage stage,
        string networkKey,
        CancellationToken ct)
    {
        var baseAddress = _options.Value.BaseAddress;
        if (string.IsNullOrWhiteSpace(baseAddress))
        {
            return NicheDiscoveryOriginRoute.Unconfigured;
        }

        var stepKey = StepKey(stage);
        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(organizationId, WorkflowKey, AnalysisType, stepKey, stepKey, networkKey),
            ct);

        var enabled = route.IsSuccess
            && route.Value.Any(candidate =>
                candidate.IsEnabled
                && string.Equals(candidate.ProviderKey, YouTubeDataApiProviderKey, StringComparison.OrdinalIgnoreCase));

        if (!enabled)
        {
            return NicheDiscoveryOriginRoute.Unconfigured;
        }

        var integration = await _integrationResolver.ResolveProviderAsync(organizationId, YouTubeDataApiProviderKey, ct);
        if (integration.IsFailure
            || !integration.Value.Credentials.TryGetValue(ApiKeyCredentialKey, out var apiKey)
            || string.IsNullOrWhiteSpace(apiKey))
        {
            return NicheDiscoveryOriginRoute.Unconfigured;
        }

        return new NicheDiscoveryOriginRoute(
            true, apiKey, baseAddress, _options.Value.SamplingRegions, _options.Value.MaxCategoriesPerRegion);
    }
}

/// <summary>
/// Resolved YouTube Data API v3 route: configured origin address + key, or Unconfigured.
///
/// <see cref="SamplingRegions"/> rides on the ROUTE rather than on the query so that region codes never
/// cross into Application or Domain. The query-less chart is region-addressed at the provider, and
/// that constraint stays where the provider lives.
/// </summary>
internal sealed record NicheDiscoveryOriginRoute(
    bool IsConfigured,
    string? ApiKey,
    string? BaseAddress,
    IReadOnlyList<string>? SamplingRegions = null,
    int? MaxCategoriesPerRegion = null)
{
    internal static readonly NicheDiscoveryOriginRoute Unconfigured = new(false, null, null);

    internal IReadOnlyList<string> Regions => SamplingRegions ?? [];

    /// <summary>Applies the per-region category cap, if one is configured.</summary>
    internal IReadOnlyList<T> CapCategories<T>(IReadOnlyList<T> categories) =>
        MaxCategoriesPerRegion is int cap && cap > 0 && categories.Count > cap
            ? categories.Take(cap).ToList()
            : categories;
}
