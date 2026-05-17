namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class TenantMembershipData
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Role { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public TenantData? Tenant { get; set; }
}
