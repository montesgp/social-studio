namespace Incoders.Hive.Domain.Tenants;

/// <summary>
/// Associates an identity user with a tenant and a role.
/// </summary>
public sealed class TenantMembership
{
    public TenantMembership(TenantId tenantId, Guid userId, TenantRole role, DateTime createdAtUtc)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        CreatedAtUtc = createdAtUtc;
    }

    public TenantId TenantId { get; }

    public Guid UserId { get; }

    public TenantRole Role { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public void ChangeRole(TenantRole role) => Role = role;
}
