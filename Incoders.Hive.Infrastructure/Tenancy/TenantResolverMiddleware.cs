using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Incoders.Hive.Infrastructure.Tenancy;

/// <summary>
/// Resolves the tenant from the request and enforces membership for authenticated users.
/// </summary>
public sealed class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolverMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentTenant currentTenant,
        ITenantResolver resolver,
        ITenantMembershipService memberships,
        ICurrentUser currentUser)
    {
        var tenantId = await resolver.ResolveAsync(context.RequestAborted);

        if (tenantId is { } resolvedTenantId)
        {
            if (currentTenant is CurrentTenant mutable)
            {
                mutable.Set(resolvedTenantId);
            }

            if (currentUser.IsAuthenticated && currentUser.UserId is { } userId)
            {
                var isMember = await memberships.IsMemberAsync(resolvedTenantId, userId, context.RequestAborted);
                if (!isMember)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://tools.ietf.org/html/rfc7807",
                        title = "Forbidden",
                        status = StatusCodes.Status403Forbidden,
                        detail = "tenants.errors.membership_required",
                        code = "tenants.membership_required",
                    }, context.RequestAborted);
                    return;
                }
            }
        }

        await _next(context);
    }
}

public static class TenantResolverApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the tenant resolution middleware to the pipeline.
    /// Place <em>after</em> authentication so membership can be validated.
    /// </summary>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolverMiddleware>();
}
