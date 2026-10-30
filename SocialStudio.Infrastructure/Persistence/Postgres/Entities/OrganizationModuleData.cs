namespace SocialStudio.Infrastructure.Persistence.Postgres.Entities;

public sealed class OrganizationModuleData
{
    public Guid OrganizationId { get; set; }

    public Guid ModuleId { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public TenantData? Organization { get; set; }

    public SystemModuleData? Module { get; set; }
}
