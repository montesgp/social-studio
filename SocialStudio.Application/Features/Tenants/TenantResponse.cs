using SocialStudio.Domain.Tenants;

namespace SocialStudio.Application.Features.Tenants;

public sealed record TenantResponse(
    Guid Id,
    string Name,
    Guid OwnerUserId,
    bool IsActive,
    DateTime? DeactivatedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static TenantResponse FromDomain(Tenant tenant) =>
        new(
            tenant.Id.Value,
            tenant.Name,
            tenant.OwnerUserId,
            tenant.IsActive,
            tenant.DeactivatedAtUtc,
            tenant.CreatedAtUtc,
            tenant.UpdatedAtUtc);
}
