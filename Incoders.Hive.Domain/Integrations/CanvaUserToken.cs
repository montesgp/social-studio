using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Integrations;

/// <summary>
/// Per-user Canva OAuth token.
/// Keyed by <c>(UserId, OrganizationIntegrationId)</c>.
///
/// <para>
/// Encrypted at rest — <see cref="EncryptedAccessToken"/> and <see cref="EncryptedRefreshToken"/>
/// are always stored as the ciphertext produced by <c>IIntegrationSecretProtector</c>.
/// Plaintext values are NEVER persisted or logged.
/// </para>
///
/// <para>
/// D2 concurrency note: <c>RowVersion</c> (mapped as xmin on Postgres) provides optimistic
/// concurrency for refresh-token rotation. See <c>EfCanvaUserTokenRepository</c> and the
/// EF configuration in <c>AppIdentityDbContext</c> for the mapping.
/// </para>
/// </summary>
public sealed class CanvaUserToken : Entity<CanvaUserTokenId>
{
    private CanvaUserToken(
        CanvaUserTokenId id,
        Guid userId,
        Guid organizationIntegrationId,
        string encryptedAccessToken,
        string encryptedRefreshToken,
        DateTime accessTokenExpiresAtUtc,
        string scopes,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        UserId = userId;
        OrganizationIntegrationId = organizationIntegrationId;
        EncryptedAccessToken = encryptedAccessToken;
        EncryptedRefreshToken = encryptedRefreshToken;
        AccessTokenExpiresAtUtc = accessTokenExpiresAtUtc;
        Scopes = scopes;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    /// <summary>The platform user that authorized Canva.</summary>
    public Guid UserId { get; private set; }

    /// <summary>FK to the org-level <c>organization_integrations</c> row for Canva.</summary>
    public Guid OrganizationIntegrationId { get; private set; }

    /// <summary>Ciphertext of the OAuth access token. Never return to UI or include in logs.</summary>
    public string EncryptedAccessToken { get; private set; }

    /// <summary>Ciphertext of the OAuth refresh token. Never return to UI or include in logs.</summary>
    public string EncryptedRefreshToken { get; private set; }

    /// <summary>UTC expiry of the access token (not the refresh token).</summary>
    public DateTime AccessTokenExpiresAtUtc { get; private set; }

    /// <summary>Space-separated OAuth scopes granted by the user.</summary>
    public string Scopes { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Row-version concurrency token. Mapped to Postgres <c>xmin</c> in EF config.
    /// Only set by the EF infrastructure layer — do not modify in domain logic.
    /// </summary>
    public uint RowVersion { get; private set; }

    /// <summary>
    /// Factory method: creates a new token for first-time Canva authorization.
    /// </summary>
    public static CanvaUserToken Create(
        CanvaUserTokenId id,
        Guid userId,
        Guid organizationIntegrationId,
        string encryptedAccessToken,
        string encryptedRefreshToken,
        DateTime accessTokenExpiresAtUtc,
        string scopes,
        DateTime nowUtc)
    {
        return new CanvaUserToken(
            id,
            userId,
            organizationIntegrationId,
            encryptedAccessToken,
            encryptedRefreshToken,
            accessTokenExpiresAtUtc,
            scopes,
            nowUtc,
            nowUtc);
    }

    /// <summary>
    /// Restores a persisted token from the database.
    /// Used by the EF repository's mapping layer — not for new tokens.
    /// </summary>
    public static CanvaUserToken Restore(
        CanvaUserTokenId id,
        Guid userId,
        Guid organizationIntegrationId,
        string encryptedAccessToken,
        string encryptedRefreshToken,
        DateTime accessTokenExpiresAtUtc,
        string scopes,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new CanvaUserToken(
            id,
            userId,
            organizationIntegrationId,
            encryptedAccessToken,
            encryptedRefreshToken,
            accessTokenExpiresAtUtc,
            scopes,
            createdAtUtc,
            updatedAtUtc);
    }

    /// <summary>
    /// Rotates the token pair after a successful Canva refresh-token exchange (D2).
    /// Bumps <see cref="UpdatedAtUtc"/>; leaves <see cref="CreatedAtUtc"/> unchanged.
    /// </summary>
    public void Rotate(
        string encryptedAccessToken,
        string encryptedRefreshToken,
        DateTime newAccessTokenExpiresAtUtc,
        DateTime nowUtc)
    {
        EncryptedAccessToken = encryptedAccessToken;
        EncryptedRefreshToken = encryptedRefreshToken;
        AccessTokenExpiresAtUtc = newAccessTokenExpiresAtUtc;
        UpdatedAtUtc = nowUtc;
    }
}
