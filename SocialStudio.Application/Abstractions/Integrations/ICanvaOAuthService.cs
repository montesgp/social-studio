using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.Integrations;

/// <summary>
/// Port: Canva per-user OAuth 2.0 PKCE flow.
///
/// <para>
/// Implementations live in Infrastructure and are provider-specific.
/// This port contains NO HttpClient, Npgsql, or ASP.NET types.
/// </para>
///
/// <para>
/// Authorization flow:
/// 1. Call <see cref="BuildAuthorizeUrlAsync"/> — generates PKCE verifier+challenge,
///    stores verifier in cache keyed by state, returns the Canva authorize URL.
/// 2. User is redirected to Canva, then back to the callback URL with code+state.
/// 3. Call <see cref="ExchangeCodeAsync"/> — validates state, exchanges code+verifier
///    for tokens, stores encrypted tokens via <c>ICanvaUserTokenRepository</c>.
/// </para>
///
/// <para>
/// D2 refresh-token race: <see cref="EnsureFreshAsync"/> implements the xmin-retry
/// pattern — on <c>DbUpdateConcurrencyException</c> it rereads the row once and uses
/// the already-rotated token WITHOUT calling Canva again.
/// </para>
/// </summary>
public interface ICanvaOAuthService
{
    /// <summary>
    /// Builds the Canva authorization URL with a freshly-generated PKCE challenge.
    /// Stores the verifier in the cache (TTL 10 min) keyed by the state parameter.
    /// </summary>
    /// <param name="userId">The platform user initiating the OAuth flow.</param>
    /// <param name="organizationIntegrationId">
    ///     The org-level Canva integration row id, used to look up client_id/secret.
    /// </param>
    /// <param name="organizationId">
    ///     The organization id, used to resolve the Canva BYOK credentials via
    ///     <c>IIntegrationResolver</c>.
    /// </param>
    /// <param name="redirectUri">The callback URL registered with Canva.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full authorize URL to which the user should be redirected.</returns>
    Task<Result<string>> BuildAuthorizeUrlAsync(
        Guid userId,
        Guid organizationIntegrationId,
        Guid organizationId,
        string redirectUri,
        CancellationToken ct = default);

    /// <summary>
    /// Validates the OAuth callback and stores the org-level Canva token.
    ///
    /// <para>
    /// The callback is a browser navigation FROM Canva — it carries NO bearer token or tenant
    /// header. Everything needed (PKCE verifier, redirect URI, organization id, integration id)
    /// was cached under <paramref name="state"/> during <see cref="BuildAuthorizeUrlAsync"/>, so
    /// this method only needs the <paramref name="code"/> and <paramref name="state"/>. The token
    /// is stored under the shared org sentinel (<c>CanvaOrgConnection.SharedUserId</c>) so the whole
    /// org reuses one connection.
    /// </para>
    /// </summary>
    /// <param name="code">The authorization code from Canva.</param>
    /// <param name="state">The state parameter returned by Canva — must match the cached entry.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> ExchangeCodeAsync(
        string code,
        string state,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the current plaintext access token for the user, refreshing it if expired
    /// (single-use refresh rotation, D2 xmin-retry).
    /// </summary>
    /// <param name="userId">The platform user whose token to retrieve.</param>
    /// <param name="organizationIntegrationId">The org-level Canva integration row id.</param>
    /// <param name="organizationId">
    ///     The REAL organization id, used to resolve the Canva BYOK credentials via
    ///     <c>IIntegrationResolver</c> when a token refresh is required.
    ///     Must be the authenticated tenant's org id — NOT the <c>organizationIntegrationId</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Plaintext access token, or a typed error (token-not-found, refresh-failed).</returns>
    Task<Result<string>> EnsureFreshAsync(
        Guid userId,
        Guid organizationIntegrationId,
        Guid organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes the user's Canva connection by deleting the stored token row.
    /// No-op if no token exists.
    /// </summary>
    /// <param name="userId">The platform user revoking their Canva connection.</param>
    /// <param name="organizationIntegrationId">The org-level Canva integration row id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RevokeAsync(
        Guid userId,
        Guid organizationIntegrationId,
        CancellationToken ct = default);
}
