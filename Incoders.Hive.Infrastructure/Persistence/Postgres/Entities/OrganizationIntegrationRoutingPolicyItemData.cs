namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class OrganizationIntegrationRoutingPolicyItemData
{
    public Guid Id { get; set; }

    public Guid PolicyId { get; set; }

    public Guid ProviderId { get; set; }

    public string ConnectionMode { get; set; } = string.Empty;

    public int Priority { get; set; }

    public bool AllowManagedFallback { get; set; }

    public int MaxAttempts { get; set; }

    public int TimeoutMs { get; set; }

    public string? SelectedModel { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public OrganizationIntegrationRoutingPolicyData? Policy { get; set; }

    public IntegrationProviderData? Provider { get; set; }
}