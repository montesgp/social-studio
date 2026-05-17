using Incoders.Hive.Domain.Tenants;

namespace Incoders.Hive.Application.Abstractions.Tenancy;

/// <summary>
/// Scoped accessor that exposes the tenant resolved for the current request.
/// </summary>
public interface ICurrentTenant
{
    TenantId? TenantId { get; }

    bool IsResolved { get; }
}
