using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SocialStudio.Infrastructure.Tenancy;

/// <summary>
/// Resolves the tenant id from the configured HTTP header (default <c>X-Tenant-Id</c>).
/// </summary>
public sealed class HeaderTenantResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptionsMonitor<MultiTenancyOptions> _options;

    public HeaderTenantResolver(
        IHttpContextAccessor httpContextAccessor,
        IOptionsMonitor<MultiTenancyOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options;
    }

    public ValueTask<TenantId?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return ValueTask.FromResult<TenantId?>(null);
        }

        var headerName = _options.CurrentValue.HeaderName;
        if (!httpContext.Request.Headers.TryGetValue(headerName, out var values))
        {
            return ValueTask.FromResult<TenantId?>(null);
        }

        var raw = values.ToString();
        if (Guid.TryParse(raw, out var guid))
        {
            return ValueTask.FromResult<TenantId?>(new TenantId(guid));
        }

        return ValueTask.FromResult<TenantId?>(null);
    }
}
