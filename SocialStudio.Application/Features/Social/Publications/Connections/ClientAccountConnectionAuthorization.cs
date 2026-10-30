using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Tenants;

namespace SocialStudio.Application.Features.Social.Publications.Connections;

/// <summary>
/// Org-scoped role gate for publish-credential management (design §15 — "Who may connect publish
/// credentials — org admin"). Kept in Application so the Api layer stays a thin adapter and the rule
/// is unit-testable without an HTTP host.
/// </summary>
public static class ClientAccountConnectionAuthorization
{
    public static async Task<bool> IsOrgAdminAsync(
        ITenantMembershipService membership, TenantId tenantId, Guid userId, CancellationToken ct)
    {
        var role = await membership.GetRoleAsync(tenantId, userId, ct);
        return role is TenantRole.Admin or TenantRole.Owner;
    }
}
