using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Incoders.Hive.ApiService.Endpoints.Common;

public static class MapMeEndpointsExtensions
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me")
            .WithTags("Me")
            .RequireAuthorization();

        group.MapGet("/", GetCurrentUser)
            .WithName("GetCurrentUser")
            .WithSummary("Returns the current authenticated user's identity including platform admin status")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/organizations", GetMyOrganizations)
            .WithName("GetMyOrganizations")
            .WithSummary("Returns the active organizations the current user belongs to")
            .Produces<IReadOnlyList<MyOrganizationResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static IResult GetCurrentUser([FromServices] ICurrentUser currentUser)
    {
        return Results.Ok(new MeResponse(
            currentUser.UserId,
            currentUser.Email,
            currentUser.IsPlatformAdmin));
    }

    private static async Task<IResult> GetMyOrganizations(
        [FromServices] ICurrentUser currentUser,
        [FromServices] AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Results.Unauthorized();

        var orgs = await db.TenantMemberships
            .Where(m => m.UserId == userId)
            .Select(m => m.Tenant!)
            .Where(t => t != null && t.IsActive)
            .Select(t => new MyOrganizationResponse(t.Id, t.Name))
            .ToListAsync(cancellationToken);

        return Results.Ok(orgs);
    }
}

public sealed record MeResponse(Guid? UserId, string? Email, bool IsPlatformAdmin);

public sealed record MyOrganizationResponse(Guid Id, string Name);
