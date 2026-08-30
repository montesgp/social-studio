namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class OrganizationActorData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid OrganizationIntegrationId { get; set; }

    public Guid ProviderId { get; set; }

    public string ProviderKey { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string MetadataJson { get; set; } = "{}";

    /// <summary>True when the connected account owns the actor; false for public-store actors it uses.</summary>
    public bool IsOwned { get; set; }

    public bool IsAvailable { get; set; }

    public DateTime SyncedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public TenantData? Organization { get; set; }

    public OrganizationIntegrationData? OrganizationIntegration { get; set; }

    public IntegrationProviderData? Provider { get; set; }
}
