namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class OrganizationIntegrationRoutingPolicyData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public string FlowKey { get; set; } = string.Empty;

    public string CapabilityKey { get; set; } = string.Empty;

    public string? PlatformKey { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public TenantData? Organization { get; set; }

    public ICollection<OrganizationIntegrationRoutingPolicyItemData> Items { get; set; } = new List<OrganizationIntegrationRoutingPolicyItemData>();
}