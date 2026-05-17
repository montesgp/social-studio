namespace Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;

public sealed class SystemSettingData
{
    public Guid Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public Guid? TenantId { get; set; }

    public Guid? UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
