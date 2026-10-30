namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

public sealed class OrganizationIntegrationData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ProviderId { get; set; }

    public string ConnectionMode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool IsReadOnly { get; set; }

    public string? SelectedAiModel { get; set; }

    public DateTime? LastTestedAtUtc { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public TenantData? Organization { get; set; }

    public IntegrationProviderData? Provider { get; set; }

    public ICollection<OrganizationIntegrationCredentialData> Credentials { get; set; } = new List<OrganizationIntegrationCredentialData>();
}