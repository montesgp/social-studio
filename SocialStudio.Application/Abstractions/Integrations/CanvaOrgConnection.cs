namespace SocialStudio.Application.Abstractions.Integrations;

/// <summary>
/// Canva is connected at the ORGANIZATION level, not per user: the org's Canva account is
/// authorized once (one OAuth — Canva Connect has no app-only/client_credentials path) and every
/// user in the org reuses that single connection for exports. No individual user is ever prompted
/// to authorize.
///
/// <para>
/// The existing per-user token table is reused with this sentinel user id, so exactly one row holds
/// the shared org connection per <c>organizationIntegrationId</c>. The PKCE verifier is correlated
/// by OAuth <c>state</c> (not by user id), so the sentinel does not affect the authorization flow.
/// </para>
/// </summary>
public static class CanvaOrgConnection
{
    /// <summary>Sentinel "user id" under which the shared org-level Canva token is stored.</summary>
    public static readonly Guid SharedUserId = Guid.Empty;
}
