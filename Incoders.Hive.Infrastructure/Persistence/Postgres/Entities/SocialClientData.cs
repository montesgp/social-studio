namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class SocialClientData
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public TenantData? Organization { get; set; }
}
