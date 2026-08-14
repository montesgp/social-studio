using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;

namespace Incoders.Hive.Infrastructure.Social.Providers;

/// <summary>
/// The single definition of "which networks can actually run a niche search" and "does an actor
/// resolve for this organization and network".
///
/// It exists because that question used to have TWO answers: the runtime resolver honoured the enabled
/// workflow-step route AND the raw <c>actor_mappings_json</c> connector entry, while the integrations
/// checklist inspected only the workflow-step row. An operator who mapped the actor in the Apify
/// connector and left the seeded workflow row empty was therefore told "not configured" while the
/// backend was already dispatching to that actor. Both call sites now go through this type.
/// </summary>
internal sealed class ApifyNicheSearchActorConfiguration
{
    internal const string ApifyProviderKey = "apify";
    internal const string CapabilityKey = "niche-search";
    internal const string WorkflowKey = "single-analyzer";
    internal const string AnalysisType = "content-discovery";
    internal const string StepKey = "discover-niche-content";
    internal const string ActorMappingsCredentialKey = "actor_mappings_json";

    /// <summary>
    /// Networks with a WORKING niche-search adapter. Instagram and YouTube are routable in the UI but
    /// have no adapter, so they degrade through <see cref="SourceOutcome.Unconfigured"/> — they are
    /// deliberately absent here rather than listed as "configurable".
    /// </summary>
    internal static readonly IReadOnlyList<NicheSearchSupportedNetwork> SupportedNetworks =
    [
        new(TikTokNetworkKey, "TikTok"),
    ];

    private const string TikTokNetworkKey = "tiktok";

    private readonly IWorkflowConfigurationResolver _workflowResolver;
    private readonly IIntegrationResolver _integrationResolver;

    internal ApifyNicheSearchActorConfiguration(
        IWorkflowConfigurationResolver workflowResolver,
        IIntegrationResolver integrationResolver)
    {
        ArgumentNullException.ThrowIfNull(workflowResolver);
        ArgumentNullException.ThrowIfNull(integrationResolver);

        _workflowResolver = workflowResolver;
        _integrationResolver = integrationResolver;
    }

    internal static bool HasAdapter(string? networkKey) =>
        SupportedNetworks.Any(x => string.Equals(x.NetworkKey, networkKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Actor resolution order (design §8.1, the ONLY way an actor id enters this process):
    ///   1. an enabled workflow-step route with a non-empty actor id;
    ///   2. otherwise the raw <c>actor_mappings_json</c> connector entry, exactly
    ///      <c>ApifyProfileScrapeResolver.cs:146-151</c>;
    ///   3. otherwise empty — never a literal id, never a throw (CD-12/CD-13, D12/D13).
    /// </summary>
    internal async Task<IReadOnlyList<string>> ResolveActorIdsAsync(
        Guid organizationId,
        string networkKey,
        CancellationToken ct)
    {
        var route = await _workflowResolver.ResolveStepAsync(
            new WorkflowStepResolutionRequest(
                organizationId,
                WorkflowKey,
                AnalysisType,
                StepKey,
                CapabilityKey,
                networkKey),
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

        var integrationResult = await _integrationResolver.ResolveProviderAsync(organizationId, ApifyProviderKey, ct);
        if (integrationResult.IsFailure
            || !integrationResult.Value.Credentials.TryGetValue(ActorMappingsCredentialKey, out var mappingsJson)
            || string.IsNullOrWhiteSpace(mappingsJson))
        {
            return [];
        }

        return ApifyActorMappingLookup.GetAllMappedActors(mappingsJson, networkKey, CapabilityKey);
    }
}

/// <summary>
/// Read-only readiness probe for the integrations overview checklist. It answers with EXACTLY the
/// resolution the runtime dispatcher performs (<see cref="ApifyNicheSearchResolver"/>), so the UI and
/// the backend can no longer disagree about whether niche search is configured.
/// </summary>
public sealed class ApifyNicheSearchConfigurationProbe : INicheSearchConfigurationProbe
{
    private readonly ApifyNicheSearchActorConfiguration _configuration;

    public ApifyNicheSearchConfigurationProbe(
        IWorkflowConfigurationResolver workflowResolver,
        IIntegrationResolver integrationResolver)
    {
        _configuration = new ApifyNicheSearchActorConfiguration(workflowResolver, integrationResolver);
    }

    public IReadOnlyList<NicheSearchSupportedNetwork> SupportedNetworks =>
        ApifyNicheSearchActorConfiguration.SupportedNetworks;

    public async Task<bool> ResolvesActorAsync(
        Guid organizationId,
        string networkKey,
        CancellationToken ct = default)
    {
        // A network with no adapter can never dispatch, so it is never "configured" — regardless of
        // what an operator may have mapped for it in the connector.
        if (!ApifyNicheSearchActorConfiguration.HasAdapter(networkKey))
        {
            return false;
        }

        var actorIds = await _configuration.ResolveActorIdsAsync(organizationId, networkKey, ct);
        return actorIds.Count > 0;
    }
}
