namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class OrganizationAiModelData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid OrganizationIntegrationId { get; set; }

    public Guid ProviderId { get; set; }

    public string ProviderKey { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string CapabilitiesJson { get; set; } = "[]";

    public string MetadataJson { get; set; } = "{}";

    public bool IsAvailable { get; set; }

    public DateTime SyncedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public TenantData? Organization { get; set; }

    public OrganizationIntegrationData? OrganizationIntegration { get; set; }

    public IntegrationProviderData? Provider { get; set; }
}

