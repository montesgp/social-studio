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

        group.MapGet("/{id:guid}", GetUserAsync)
            .WithName("AdminGetUser")
            .WithSummary("Get one user with status and roles (sysadmin only)")
            .Produces<AdminUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateUserAsync)
            .WithName("AdminCreateUser")
            .WithSummary("Create a user account and assign roles (sysadmin only)")
            .Produces<AdminUserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", UpdateUserAsync)
            .WithName("AdminUpdateUser")
            .WithSummary("Update a user profile, status and roles (sysadmin only)")
            .Produces<AdminUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

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
            result.Add(await ToResponseAsync(user, users));
        }
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetUserAsync(Guid id, UserManager<ApplicationUser> users)
    {
        var user = await users.FindByIdAsync(id.ToString());
        return user is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found")
            : TypedResults.Ok(await ToResponseAsync(user, users));
    }

    private static async Task<IResult> CreateUserAsync(
        [FromBody] CreateAdminUserRequest body,
        UserManager<ApplicationUser> users)
    {
        var rolesValidation = ValidateRoles(body.Roles);
        if (rolesValidation is not null)
        {
            return rolesValidation;
        }

        var user = new ApplicationUser
        {
            UserName = body.Email,
            Email = body.Email,
            DisplayName = body.DisplayName,
            Status = body.Status,
        };

        var create = await users.CreateAsync(user, body.Password);
        if (!create.Succeeded)
        {
            return BuildIdentityProblem(create);
        }

        var roles = NormalizeRoles(body.Roles);
        if (roles.Count > 0)
        {
            var addRoles = await users.AddToRolesAsync(user, roles);
            if (!addRoles.Succeeded)
            {
                return BuildIdentityProblem(addRoles);
            }
        }

        return TypedResults.Created($"/api/admin/users/{user.Id}", await ToResponseAsync(user, users));
    }

    private static async Task<IResult> UpdateUserAsync(
        Guid id,
        [FromBody] UpdateAdminUserRequest body,
        UserManager<ApplicationUser> users)
    {
        var rolesValidation = ValidateRoles(body.Roles);
        if (rolesValidation is not null)
        {
            return rolesValidation;
        }

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found");
        }

        user.Email = body.Email;
        user.UserName = body.Email;
        user.DisplayName = body.DisplayName;
        user.Status = body.Status;

        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            return BuildIdentityProblem(update);
        }

        var currentRoles = await users.GetRolesAsync(user);
        var nextRoles = NormalizeRoles(body.Roles);
        var remove = currentRoles.Except(nextRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        var add = nextRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();

        if (remove.Length > 0)
        {
            var removeResult = await users.RemoveFromRolesAsync(user, remove);
            if (!removeResult.Succeeded)
            {
                return BuildIdentityProblem(removeResult);
            }
        }

        if (add.Length > 0)
        {
            var addResult = await users.AddToRolesAsync(user, add);
            if (!addResult.Succeeded)
            {
                return BuildIdentityProblem(addResult);
            }
        }

        return TypedResults.Ok(await ToResponseAsync(user, users));
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

    private static async Task<AdminUserResponse> ToResponseAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> users)
    {
        var roles = await users.GetRolesAsync(user);
        return new AdminUserResponse(user.Id, user.Email!, user.DisplayName, user.Status, [.. roles.Order()]);
    }

    private static IResult? ValidateRoles(IReadOnlyList<string> roles)
    {
        var invalid = NormalizeRoles(roles)
            .Where(role => !IdentityRoles.All.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return invalid.Length == 0
            ? null
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["roles"] = [$"Unsupported roles: {string.Join(", ", invalid)}"],
            });
    }

    private static List<string> NormalizeRoles(IReadOnlyList<string> roles) =>
        roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToList();

    private static IResult BuildIdentityProblem(IdentityResult result)
    {
        var errors = result.Errors
            .GroupBy(error => error.Code)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray());

        return TypedResults.ValidationProblem(errors);
    }
}

public sealed record CreateAdminUserRequest(
    string Email,
    string? DisplayName,
    string Password,
    UserStatus Status,
    IReadOnlyList<string> Roles);

public sealed record UpdateAdminUserRequest(
    string Email,
    string? DisplayName,
    UserStatus Status,
    IReadOnlyList<string> Roles);

public sealed record SetUserStatusRequest(UserStatus Status);

public sealed record AdminUserResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    UserStatus Status,
    string[] Roles);
