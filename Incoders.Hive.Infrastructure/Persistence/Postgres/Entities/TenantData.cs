namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class TenantData
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid OwnerUserId { get; set; }

    public bool IsActive { get; set; }

    public DateTime? DeactivatedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
