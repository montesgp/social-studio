using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// Checklist-readiness probe (mirrors <c>ApifyNicheSearchConfigurationProbe</c>): reports satisfied
/// once the API key is present AND at least one stage route resolves — exactly the resolution the
/// runtime origin performs, so the checklist and the dispatcher can never disagree.
/// </summary>
public sealed class NicheDiscoveryOriginProbe : INicheDiscoveryOriginProbe
{
    private const string SupportedNetworkKey = "youtube";

    private readonly NicheDiscoveryOriginConfiguration _configuration;

    public NicheDiscoveryOriginProbe(
        IWorkflowConfigurationResolver workflowResolver,
        IIntegrationResolver integrationResolver,
        IOptions<NicheDiscoveryOriginOptions> options)
    {
        _configuration = new NicheDiscoveryOriginConfiguration(workflowResolver, integrationResolver, options);
    }

    public async Task<bool> IsReadyAsync(Guid organizationId, string networkKey, CancellationToken ct = default)
    {
        if (!string.Equals(networkKey, SupportedNetworkKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The channel-search stage is the entry point of every run — if it does not resolve, no run
        // can ever start, so it is the representative stage for readiness.
        var route = await _configuration.ResolveAsync(organizationId, DiscoveryStage.ChannelSearch, networkKey, ct);
        return route.IsConfigured;
    }
}
