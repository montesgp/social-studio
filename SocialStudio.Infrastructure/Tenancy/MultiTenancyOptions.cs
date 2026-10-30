namespace SocialStudio.Infrastructure.Tenancy;

public sealed class MultiTenancyOptions
{
    public const string SectionName = "MultiTenancy";

    public bool Enabled { get; set; } = true;

    public string HeaderName { get; set; } = "X-Tenant-Id";
}
