namespace SocialStudio.Application.Abstractions.Social.ContentDiscovery;

/// <summary>
/// Answers ONE question for read-only surfaces (the integrations overview checklist): does a
/// niche-search actor actually resolve for this <c>(organization, network)</c> pair?
///
/// It exists because the readiness signal and the runtime dispatcher were answering that question
/// differently. <see cref="INicheSearchResolver"/> resolves an actor from TWO configuration sources —
/// the enabled workflow-step route AND the raw Apify connector actor mapping — while the checklist used
/// to inspect only the first, so an operator whose actor was mapped in the connector was told
/// "not configured" while the backend happily called that actor. Both now go through the same
/// implementation, so the two definitions of "configured" cannot drift again.
///
/// Deliberately Apify-free: the Application layer asks about networks and organizations, never about
/// actor ids, credentials or mapping JSON. The implementation lives in Infrastructure.
/// </summary>
public interface INicheSearchConfigurationProbe
{
    /// <summary>
    /// The networks that have a WORKING niche-search adapter today. A network absent from this list is
    /// not merely unconfigured — it cannot work at all, so callers must say "not supported yet" instead
    /// of asking the operator to configure an actor that would never be dispatched.
    /// </summary>
    IReadOnlyList<NicheSearchSupportedNetwork> SupportedNetworks { get; }

    /// <summary>
    /// True when a niche-search actor resolves for this organization and network through ANY of the
    /// configuration sources the runtime resolver honours. Returns false (never throws) for a network
    /// that has no adapter.
    /// </summary>
    Task<bool> ResolvesActorAsync(Guid organizationId, string networkKey, CancellationToken ct = default);
}

/// <param name="NetworkKey">Routing key, e.g. <c>tiktok</c>.</param>
/// <param name="DisplayName">Operator-facing name used in checklist copy, e.g. <c>TikTok</c>.</param>
public sealed record NicheSearchSupportedNetwork(string NetworkKey, string DisplayName);
