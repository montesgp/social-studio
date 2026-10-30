namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF persistence model for <c>platform.canva_user_tokens</c>.
/// No EF data annotations — all mapping is done in <c>AppIdentityDbContext.ConfigureApplicationTables</c>.
/// </summary>
public sealed class CanvaUserTokenData
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>FK to <c>platform.organization_integrations(id)</c>.</summary>
    public Guid OrganizationIntegrationId { get; set; }

    /// <summary>Ciphertext of the OAuth access token.</summary>
    public string EncryptedAccessToken { get; set; } = string.Empty;

    /// <summary>Ciphertext of the OAuth refresh token.</summary>
    public string EncryptedRefreshToken { get; set; } = string.Empty;

    public DateTime AccessTokenExpiresAtUtc { get; set; }

    /// <summary>Space-separated OAuth scopes.</summary>
    public string Scopes { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Postgres xmin system column — mapped as optimistic concurrency token (D2).
    /// Set automatically by Postgres on every row write; EF reads it back after save.
    /// </summary>
    public uint RowVersion { get; set; }
}
