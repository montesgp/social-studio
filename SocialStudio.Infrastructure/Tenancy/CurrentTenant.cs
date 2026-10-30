using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Tenants;

namespace SocialStudio.Infrastructure.Tenancy;

/// <summary>
/// Scoped mutable holder set by <see cref="TenantResolverMiddleware"/>.
/// </summary>
public sealed class CurrentTenant : ICurrentTenant
{
    public TenantId? TenantId { get; private set; }

    public bool IsResolved => TenantId is not null;

    internal void Set(TenantId tenantId) => TenantId = tenantId;
}
