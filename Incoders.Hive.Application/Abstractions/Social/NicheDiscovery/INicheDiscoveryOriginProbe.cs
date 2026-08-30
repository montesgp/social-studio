namespace Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;

/// <summary>
/// Checklist-readiness port (mirrors <c>INicheSearchConfigurationProbe</c>): does a niche-discovery
/// origin actually resolve for this <c>(organization, network)</c> pair? Implemented in Infrastructure
/// (WU-5) and consumed by the "niche-discovery" checklist group (WU-4).
/// </summary>
public interface INicheDiscoveryOriginProbe
{
    /// <summary>True when at least one configured origin route resolves. Never throws.</summary>
    Task<bool> IsReadyAsync(Guid organizationId, string networkKey, CancellationToken ct = default);
}
