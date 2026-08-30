namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// EF Core persistence entity for <c>social.client_account_connections</c>.
/// No annotations — all configuration is done via fluent API in AppIdentityDbContext.
/// <see cref="EncryptedAccessToken"/> is ciphertext only (DataProtection). Unique on
/// <c>(organization_id, client_id, social_network_id)</c> — one publish account per client per
/// network in v1.
/// </summary>
public sealed class ClientAccountConnectionData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClientId { get; set; }

    public Guid SocialNetworkId { get; set; }

    // Org-level app this account authorizes against
    public Guid OrganizationIntegrationId { get; set; }

    public string ExternalAccountId { get; set; } = string.Empty;

    public string EncryptedAccessToken { get; set; } = string.Empty;

    public DateTime? TokenExpiresAtUtc { get; set; }

    public short Status { get; set; }

    public DateTime? LastTestedAtUtc { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
