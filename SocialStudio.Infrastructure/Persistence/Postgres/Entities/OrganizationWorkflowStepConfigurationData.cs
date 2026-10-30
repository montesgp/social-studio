namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

public sealed class OrganizationWorkflowStepConfigurationData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public string WorkflowKey { get; set; } = string.Empty;

    public string AnalysisType { get; set; } = string.Empty;

    public string StepKey { get; set; } = string.Empty;

    public string? OperationKey { get; set; }

    public string? NetworkKey { get; set; }

    public Guid ProviderId { get; set; }

    public string ProviderKey { get; set; } = string.Empty;

    public Guid OrganizationIntegrationId { get; set; }

    public string? ModelId { get; set; }

    public string? ActorId { get; set; }

    public int Priority { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public TenantData? Organization { get; set; }

    public OrganizationIntegrationData? OrganizationIntegration { get; set; }

    public IntegrationProviderData? Provider { get; set; }
}

