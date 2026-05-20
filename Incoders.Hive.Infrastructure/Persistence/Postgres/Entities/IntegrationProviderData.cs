namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class IntegrationProviderData
{
    public Guid Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string IconKey { get; set; } = string.Empty;

    public bool SupportsBringYourOwnKey { get; set; }

    public bool SupportsManagedByIncoders { get; set; }

    public bool SupportsSystemDefault { get; set; }

    public bool SystemDefaultReadOnly { get; set; }

    public string CredentialFieldsJson { get; set; } = "[]";

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<OrganizationIntegrationData> OrganizationIntegrations { get; set; } = new List<OrganizationIntegrationData>();

    public ICollection<IntegrationProviderCapabilityData> Capabilities { get; set; } = new List<IntegrationProviderCapabilityData>();

    public ICollection<OrganizationIntegrationRoutingPolicyItemData> RoutingPolicyItems { get; set; } = new List<OrganizationIntegrationRoutingPolicyItemData>();
}
