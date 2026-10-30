using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.Infrastructure.Social.Canva;

/// <summary>
/// Cached PKCE + context for an in-flight Canva authorization, keyed by the OAuth <c>state</c>.
/// Stored at authorize time (when the request is authenticated) so the unauthenticated callback
/// navigation from Canva can rebuild everything it needs from <c>state</c> alone.
/// </summary>
public sealed record CanvaPendingAuthorization(
    string Verifier,
    string RedirectUri,
    Guid OrganizationId,
    Guid OrganizationIntegrationId);

/// <summary>
/// Infrastructure adapter for Canva per-user OAuth 2.0 PKCE flow.
///
/// <para>
/// Implements <see cref="ICanvaOAuthService"/>. Handles PKCE generation, authorization URL
/// building, code-exchange, single-use refresh-token rotation, and the D2 xmin-retry pattern.
/// </para>
///
/// <para>
/// D2 (xmin retry): on <see cref="DbUpdateConcurrencyException"/> during <c>UpsertAsync</c>,
/// the service re-reads the row once and returns the already-rotated token WITHOUT calling
/// the Canva token endpoint a second time.
/// </para>
/// </summary>
public sealed class CanvaOAuthService : ICanvaOAuthService
{
    // Canva Connect OAuth endpoints (verified against Canva Connect API docs)
    private const string CanvaAuthorizeUrl = "https://www.canva.com/api/oauth/authorize";
    private const string CanvaTokenUrl = "https://api.canva.com/rest/v1/oauth/token";

    private const string PkceCacheKeyPrefix = "canva:pkce:";
    private static readonly TimeSpan PkceTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Default Connect API scopes used when the org leaves the <c>scopes</c> credential blank.
    /// Mirrors the placeholder advertised by the Canva connector seeder ("Leave blank to use the
    /// default set"). A blank scope makes Canva reject the authorization / yields a token that the
    /// Design Import API (<c>design:content:write</c>) cannot use — so a sensible default is required.
    /// </summary>
    private const string DefaultScopes =
        "asset:read asset:write brandtemplate:meta:read design:meta:read design:content:read design:content:write profile:read";

    private readonly ICanvaUserTokenRepository _tokenRepository;
    private readonly ICacheStore _cache;
    private readonly IIntegrationResolver _integrationResolver;
    private readonly IIntegrationSecretProtector _protector;
    private readonly IUnitOfWork _unitOfWork;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    public CanvaOAuthService(
        ICanvaUserTokenRepository tokenRepository,
        ICacheStore cache,
        IIntegrationResolver integrationResolver,
        IIntegrationSecretProtector protector,
        IUnitOfWork unitOfWork,
        HttpClient httpClient,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(tokenRepository);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(integrationResolver);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _tokenRepository = tokenRepository;
        _cache = cache;
        _integrationResolver = integrationResolver;
        _protector = protector;
        _unitOfWork = unitOfWork;
        _httpClient = httpClient;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<Result<string>> BuildAuthorizeUrlAsync(
        Guid userId,
        Guid organizationIntegrationId,
        Guid organizationId,
        string redirectUri,
        CancellationToken ct = default)
    {
        var resolved = await _integrationResolver.ResolveProviderAsync(organizationId, "canva", ct);
        if (resolved.IsFailure)
            return Result.Failure<string>(resolved.Error);

        var clientId = resolved.Value.Credentials.GetValueOrDefault("client_id", string.Empty);
        var scopes = resolved.Value.Credentials.GetValueOrDefault("scopes", string.Empty);

        if (string.IsNullOrWhiteSpace(clientId))
            return Result.Failure<string>(CanvaOAuthErrors.ClientIdMissing);

        // The admin may leave scopes blank (it is an optional credential). Fall back to the default
        // Connect scope set so the authorization is never sent with an empty scope — which Canva
        // rejects and which would otherwise yield a token unusable for Design Import.
        if (string.IsNullOrWhiteSpace(scopes))
            scopes = DefaultScopes;

        // PKCE: generate verifier (random 32-byte / Base64Url → 43-char URL-safe string)
        var verifier = GeneratePkceVerifier();
        var challenge = ComputePkceChallenge(verifier);

        // Generate state — opaque random value used to prevent CSRF
        var state = GenerateState();

        // Cache everything the callback needs, keyed by state (TTL 10 min). The callback is an
        // unauthenticated browser navigation from Canva, so it rebuilds the full context from here.
        await _cache.SetAsync(
            PkceCacheKeyPrefix + state,
            new CanvaPendingAuthorization(verifier, redirectUri, organizationId, organizationIntegrationId),
            PkceTtl,
            ct);

        var url = BuildAuthorizeUrl(clientId, scopes, challenge, state, redirectUri);
        return Result.Success(url);
    }

    /// <inheritdoc/>
    public async Task<Result> ExchangeCodeAsync(
        string code,
        string state,
        CancellationToken ct = default)
    {
        // Validate state and retrieve the cached authorization context (verifier + redirect URI +
        // org ids). Everything was stored at authorize time when we had the authenticated context.
        var pending = await _cache.GetAsync<CanvaPendingAuthorization>(PkceCacheKeyPrefix + state, ct);
        if (pending is null)
            return Result.Failure(CanvaOAuthErrors.InvalidState);

        // Clean up the state from cache (single-use)
        await _cache.RemoveAsync(PkceCacheKeyPrefix + state, ct);

        // Resolve org credentials to get client_id/secret
        var resolved = await _integrationResolver.ResolveProviderAsync(pending.OrganizationId, "canva", ct);
        if (resolved.IsFailure)
            return Result.Failure(resolved.Error);

        var clientId = resolved.Value.Credentials.GetValueOrDefault("client_id", string.Empty);
        var clientSecret = resolved.Value.Credentials.GetValueOrDefault("client_secret", string.Empty);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return Result.Failure(CanvaOAuthErrors.ClientCredentialsMissing);

        // Exchange code for tokens (redirect URI must match the one used at authorize time).
        var tokenResult = await ExchangeAuthorizationCodeAsync(
            clientId, clientSecret, code, pending.Verifier, pending.RedirectUri, ct);
        if (tokenResult.IsFailure)
            return Result.Failure(tokenResult.Error);

        var tokens = tokenResult.Value;

        // Encrypt tokens before persisting
        var encryptedAccess = _protector.Protect(tokens.AccessToken);
        var encryptedRefresh = _protector.Protect(tokens.RefreshToken);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = nowUtc.AddSeconds(tokens.ExpiresIn - 60); // 60s buffer

        // Org-level connection: store under the shared sentinel so the whole org reuses one token.
        var existing = await _tokenRepository.GetAsync(
            CanvaOrgConnection.SharedUserId, pending.OrganizationIntegrationId, ct);

        CanvaUserToken tokenEntity;
        if (existing is null)
        {
            tokenEntity = CanvaUserToken.Create(
                CanvaUserTokenId.New(),
                CanvaOrgConnection.SharedUserId,
                pending.OrganizationIntegrationId,
                encryptedAccess,
                encryptedRefresh,
                expiresAt,
                tokens.Scope,
                nowUtc);
        }
        else
        {
            existing.Rotate(encryptedAccess, encryptedRefresh, expiresAt, nowUtc);
            tokenEntity = existing;
        }

        await _tokenRepository.UpsertAsync(tokenEntity, ct);

        // Commit the unit of work — the repository only tracks the change; without this the token
        // row is discarded when the request scope ends (the bug that left canva_user_tokens empty
        // even after a successful authorize, yielding canva.not_connected / 409 on export).
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result<string>> EnsureFreshAsync(
        Guid userId,
        Guid organizationIntegrationId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var token = await _tokenRepository.GetAsync(userId, organizationIntegrationId, ct);
        if (token is null)
            return Result.Failure<string>(CanvaOAuthErrors.TokenNotFound);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // Access token still valid — just decrypt and return
        if (token.AccessTokenExpiresAtUtc > nowUtc)
            return Result.Success(_protector.Unprotect(token.EncryptedAccessToken));

        // Token expired — need to refresh via single-use refresh token (D2 rotation).
        // Pass the REAL organizationId (not organizationIntegrationId as a proxy)
        // so the resolver can load the correct BYOK client_id/secret.
        var refreshToken = _protector.Unprotect(token.EncryptedRefreshToken);
        var refreshResult = await RefreshAccessTokenAsync(token, refreshToken, nowUtc, organizationId, ct);
        return refreshResult;
    }

    /// <inheritdoc/>
    public async Task RevokeAsync(
        Guid userId,
        Guid organizationIntegrationId,
        CancellationToken ct = default)
    {
        await _tokenRepository.DeleteAsync(userId, organizationIntegrationId, ct);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task<Result<string>> RefreshAccessTokenAsync(
        CanvaUserToken token,
        string plaintextRefreshToken,
        DateTime nowUtc,
        Guid organizationId,
        CancellationToken ct)
    {
        // Resolve the org BYOK credentials (client_id / client_secret) using the REAL organizationId.
        // This is the correct pattern: callers of EnsureFreshAsync must supply the authenticated
        // tenant's org id, NOT the organizationIntegrationId (which is a different entity).
        // The resolver returns the Canva app credentials needed for the refresh grant.
        var resolved = await _integrationResolver.ResolveProviderAsync(
            organizationId,
            "canva",
            ct);

        string clientId;
        string clientSecret;

        if (resolved.IsSuccess)
        {
            clientId = resolved.Value.Credentials.GetValueOrDefault("client_id", string.Empty);
            clientSecret = resolved.Value.Credentials.GetValueOrDefault("client_secret", string.Empty);
        }
        else
        {
            // Fall back: use empty credentials — the Canva token endpoint will return 401.
            clientId = string.Empty;
            clientSecret = string.Empty;
        }

        var tokenResult = await CallRefreshGrantAsync(clientId, clientSecret, plaintextRefreshToken, ct);
        if (tokenResult.IsFailure)
            return Result.Failure<string>(tokenResult.Error);

        var newTokens = tokenResult.Value;
        var encryptedAccess = _protector.Protect(newTokens.AccessToken);
        var encryptedRefresh = _protector.Protect(newTokens.RefreshToken);
        var expiresAt = nowUtc.AddSeconds(newTokens.ExpiresIn - 60);

        token.Rotate(encryptedAccess, encryptedRefresh, expiresAt, nowUtc);

        try
        {
            await _tokenRepository.UpsertAsync(token, ct);
            // Commit the rotation. SaveChanges is also where the xmin (D2) concurrency conflict
            // surfaces — keep it inside the try so the reread-on-conflict path below handles it.
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success(newTokens.AccessToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // D2: another request already committed the rotation.
            // Re-read the row once — use whatever is in the DB (already rotated).
            // Do NOT call Canva again.
            var reread = await _tokenRepository.GetAsync(token.UserId, token.OrganizationIntegrationId, ct);
            if (reread is null)
                return Result.Failure<string>(CanvaOAuthErrors.TokenNotFound);

            return Result.Success(_protector.Unprotect(reread.EncryptedAccessToken));
        }
    }

    private async Task<Result<CanvaTokenResponse>> ExchangeAuthorizationCodeAsync(
        string clientId,
        string clientSecret,
        string code,
        string verifier,
        string redirectUri,
        CancellationToken ct)
    {
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri,
        });

        return await PostToTokenEndpointAsync(clientId, clientSecret, formContent, ct);
    }

    private async Task<Result<CanvaTokenResponse>> CallRefreshGrantAsync(
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct)
    {
        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });

        return await PostToTokenEndpointAsync(clientId, clientSecret, formContent, ct);
    }

    private async Task<Result<CanvaTokenResponse>> PostToTokenEndpointAsync(
        string clientId,
        string clientSecret,
        FormUrlEncodedContent formContent,
        CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, CanvaTokenUrl)
        {
            Content = formContent,
        };

        // Basic auth: Base64(client_id:client_secret) per RFC 6749
        if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result.Failure<CanvaTokenResponse>(CanvaOAuthErrors.TokenEndpointUnreachable);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<CanvaTokenResponse>(
                response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? CanvaOAuthErrors.TokenEndpointUnauthorized
                    : CanvaOAuthErrors.RefreshFailed);
        }

        var json = await response.Content.ReadAsStringAsync(ct);

        CanvaTokenResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CanvaTokenResponse>(json);
        }
        catch
        {
            return Result.Failure<CanvaTokenResponse>(CanvaOAuthErrors.RefreshFailed);
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.AccessToken))
            return Result.Failure<CanvaTokenResponse>(CanvaOAuthErrors.RefreshFailed);

        return Result.Success(parsed);
    }

    private static string BuildAuthorizeUrl(
        string clientId,
        string scopes,
        string challenge,
        string state,
        string redirectUri)
    {
        // RFC 6749 / Canva Connect authorize endpoint parameters
        var sb = new StringBuilder(CanvaAuthorizeUrl);
        sb.Append("?response_type=code");
        sb.Append("&client_id=").Append(Uri.EscapeDataString(clientId));
        sb.Append("&scope=").Append(Uri.EscapeDataString(scopes));
        sb.Append("&code_challenge=").Append(Uri.EscapeDataString(challenge));
        sb.Append("&code_challenge_method=S256");
        sb.Append("&state=").Append(Uri.EscapeDataString(state));
        sb.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
        return sb.ToString();
    }

    private static string GeneratePkceVerifier()
    {
        // 32 random bytes → Base64Url (no padding) → 43-char URL-safe string
        // This produces exactly 43 characters, within the PKCE spec [43, 128] range.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string ComputePkceChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string GenerateState()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    // ─── Private response DTO ─────────────────────────────────────────────────

    private sealed class CanvaTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; init; } = string.Empty;

        [JsonPropertyName("scope")]
        public string Scope { get; init; } = string.Empty;
    }
}

/// <summary>Canva OAuth domain errors.</summary>
public static class CanvaOAuthErrors
{
    public static readonly Error InvalidState = Error.Validation(
        "canva.oauth.invalid_state",
        "canva.errors.oauth.invalid_state");

    public static readonly Error ClientIdMissing = Error.Validation(
        "canva.oauth.client_id_missing",
        "canva.errors.oauth.client_id_missing");

    public static readonly Error ClientCredentialsMissing = Error.Validation(
        "canva.oauth.client_credentials_missing",
        "canva.errors.oauth.client_credentials_missing");

    public static readonly Error TokenNotFound = Error.NotFound(
        "canva.oauth.token_not_found",
        "canva.errors.oauth.token_not_found");

    public static readonly Error RefreshFailed = Error.Failure(
        "canva.oauth.refresh_failed",
        "canva.errors.oauth.refresh_failed");

    public static readonly Error TokenEndpointUnreachable = Error.Failure(
        "canva.oauth.token_endpoint_unreachable",
        "canva.errors.oauth.token_endpoint_unreachable");

    public static readonly Error TokenEndpointUnauthorized = Error.Failure(
        "canva.oauth.token_endpoint_unauthorized",
        "canva.errors.oauth.token_endpoint_unauthorized");
}
