using SocialStudio.Domain.Tenants;

namespace SocialStudio.Application.Abstractions.Tenancy;

/// <summary>
/// Resolves a <see cref="TenantId"/> for the current request, typically from an HTTP header.
/// </summary>
public interface ITenantResolver
{
    /// <returns>The resolved tenant id, or <c>null</c> if the request does not carry one.</returns>
    ValueTask<TenantId?> ResolveAsync(CancellationToken cancellationToken = default);
}
