using Incoders.Hive.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.Admin;

public static class AdminUsersEndpointsExtensions
{
    public static IEndpointRouteBuilder MapAdminUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
            .WithTags("Admin")
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin);

        group.MapGet("/", ListUsersAsync)
            .WithName("AdminListUsers")
            .WithSummary("List all users with their status and roles (sysadmin only)")
            .Produces<List<AdminUserResponse>>(StatusCodes.Status200OK);

        group.MapPut("/{id:guid}/status", SetStatusAsync)
            .WithName("AdminSetUserStatus")
            .WithSummary("Enable or suspend a user account (sysadmin only)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListUsersAsync(UserManager<ApplicationUser> users)
    {
        var result = new List<AdminUserResponse>();
        foreach (var user in users.Users.OrderBy(u => u.Email).ToList())
        {
            var roles = await users.GetRolesAsync(user);
            result.Add(new AdminUserResponse(user.Id, user.Email!, user.DisplayName, user.Status, [.. roles]));
        }
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> SetStatusAsync(
        Guid id,
        [FromBody] SetUserStatusRequest body,
        UserManager<ApplicationUser> users)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found");
        }

        user.Status = body.Status;
        await users.UpdateAsync(user);

        return TypedResults.NoContent();
    }
}

public sealed record SetUserStatusRequest(UserStatus Status);

public sealed record AdminUserResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    UserStatus Status,
    string[] Roles);
