namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

public sealed class IntegrationProviderCapabilityData
{
    public Guid Id { get; set; }

    public Guid ProviderId { get; set; }

    public string CapabilityKey { get; set; } = string.Empty;

    public string? PlatformKey { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public IntegrationProviderData? Provider { get; set; }
}