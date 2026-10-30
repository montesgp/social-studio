using SocialStudio.Domain.Common;

namespace SocialStudio.Domain.Tenants;

/// <summary>
/// Aggregate root representing a tenant / organization in the shared-database, tenant-column isolation model.
/// </summary>
public sealed class Tenant : Entity<TenantId>
{
    public const int NameMaxLength = 200;

    private Tenant(
        TenantId id,
        string name,
        Guid ownerUserId,
        bool isActive,
        DateTime? deactivatedAtUtc,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        Name = name;
        OwnerUserId = ownerUserId;
        IsActive = isActive;
        DeactivatedAtUtc = deactivatedAtUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public string Name { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime? DeactivatedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<Tenant> Create(TenantId id, string name, Guid ownerUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Tenant>(TenantErrors.NameRequired);
        }

        if (ownerUserId == Guid.Empty)
        {
            return Result.Failure<Tenant>(TenantErrors.OwnerRequired);
        }

        if (name.Length > NameMaxLength)
        {
            return Result.Failure<Tenant>(TenantErrors.NameTooLong);
        }

        return Result.Success(new Tenant(id, name.Trim(), ownerUserId, true, null, nowUtc, nowUtc));
    }

    public static Tenant Restore(
        TenantId id,
        string name,
        Guid ownerUserId,
        bool isActive,
        DateTime? deactivatedAtUtc,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new Tenant(id, name, ownerUserId, isActive, deactivatedAtUtc, createdAtUtc, updatedAtUtc);
    }

    public Result Rename(string name, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(TenantErrors.NameRequired);
        }

        if (name.Length > NameMaxLength)
        {
            return Result.Failure(TenantErrors.NameTooLong);
        }

        Name = name.Trim();
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Deactivate(DateTime nowUtc)
    {
        if (!IsActive)
        {
            return Result.Failure(TenantErrors.AlreadyInactive);
        }

        IsActive = false;
        DeactivatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Activate(DateTime nowUtc)
    {
        if (IsActive)
        {
            return Result.Failure(TenantErrors.AlreadyActive);
        }

        IsActive = true;
        DeactivatedAtUtc = null;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}
