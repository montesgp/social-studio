namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class OrganizationIntegrationCredentialData
{
    public Guid Id { get; set; }

    public Guid OrganizationIntegrationId { get; set; }

    public string Key { get; set; } = string.Empty;

    public string EncryptedValue { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public OrganizationIntegrationData? OrganizationIntegration { get; set; }
}