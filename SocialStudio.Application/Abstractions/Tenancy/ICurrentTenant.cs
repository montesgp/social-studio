using SocialStudio.Domain.Tenants;

namespace SocialStudio.Application.Abstractions.Tenancy;

/// <summary>
/// Scoped accessor that exposes the tenant resolved for the current request.
/// </summary>
public interface ICurrentTenant
{
    TenantId? TenantId { get; }

    bool IsResolved { get; }
}
