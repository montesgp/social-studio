using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Entities;
using Incoders.Hive.Domain.Tenants;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    private static async Task<IResult> ListUsersAsync(
        UserManager<ApplicationUser> users,
        AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var result = new List<AdminUserResponse>();
        var allUsers = await users.Users
            .OrderBy(u => u.Email)
            .ToListAsync(cancellationToken);

        foreach (var user in allUsers)
        {
            result.Add(await ToResponseAsync(user, users, db, cancellationToken));
        }

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetUserAsync(
        Guid id,
        UserManager<ApplicationUser> users,
        AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString());
        return user is null
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "User not found")
            : TypedResults.Ok(await ToResponseAsync(user, users, db, cancellationToken));
    }

    private static async Task<IResult> CreateUserAsync(
        [FromBody] CreateAdminUserRequest body,
        UserManager<ApplicationUser> users,
        AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var rolesValidation = ValidateRoles(body.Roles);
        if (rolesValidation is not null)
        {
            return rolesValidation;
        }

        var roles = NormalizeRoles(body.Roles);
        var organizationModulesValidation = await ValidateOrganizationModulesAsync(
            body.OrganizationModules,
            roles,
            db,
            cancellationToken);
        if (organizationModulesValidation is not null)
        {
            return organizationModulesValidation;
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

        if (roles.Count > 0)
        {
            var addRoles = await users.AddToRolesAsync(user, roles);
            if (!addRoles.Succeeded)
            {
                return BuildIdentityProblem(addRoles);
            }
        }

        await SaveOrganizationModulesAsync(user.Id, roles.Contains(IdentityRoles.Sysadmin) ? [] : body.OrganizationModules, db, cancellationToken);
        return TypedResults.Created($"/api/admin/users/{user.Id}", await ToResponseAsync(user, users, db, cancellationToken));
    }

    private static async Task<IResult> UpdateUserAsync(
        Guid id,
        [FromBody] UpdateAdminUserRequest body,
        UserManager<ApplicationUser> users,
        AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var rolesValidation = ValidateRoles(body.Roles);
        if (rolesValidation is not null)
        {
            return rolesValidation;
        }

        var nextRoles = NormalizeRoles(body.Roles);
        var organizationModulesValidation = await ValidateOrganizationModulesAsync(
            body.OrganizationModules,
            nextRoles,
            db,
            cancellationToken);
        if (organizationModulesValidation is not null)
        {
            return organizationModulesValidation;
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

        await SaveOrganizationModulesAsync(user.Id, nextRoles.Contains(IdentityRoles.Sysadmin) ? [] : body.OrganizationModules, db, cancellationToken);
        return TypedResults.Ok(await ToResponseAsync(user, users, db, cancellationToken));
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
        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            return BuildIdentityProblem(update);
        }

        return TypedResults.NoContent();
    }

    private static async Task<AdminUserResponse> ToResponseAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> users,
        AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var roles = await users.GetRolesAsync(user);

        var membershipRows = await db.TenantMemberships
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Join(
                db.Tenants.AsNoTracking(),
                membership => membership.TenantId,
                organization => organization.Id,
                (membership, organization) => new
                {
                    OrganizationId = membership.TenantId,
                    OrganizationName = organization.Name,
                })
            .OrderBy(x => x.OrganizationName)
            .ToListAsync(cancellationToken);

        var membershipOrganizationIds = membershipRows
            .Select(x => x.OrganizationId)
            .ToHashSet();

        var modulesRows = await db.OrganizationUserModules
            .AsNoTracking()
            .Where(x => x.UserId == user.Id && membershipOrganizationIds.Contains(x.OrganizationId))
            .Join(
                db.Tenants.AsNoTracking(),
                assignment => assignment.OrganizationId,
                organization => organization.Id,
                (assignment, organization) => new { assignment, organization })
            .Join(
                db.SystemModules.AsNoTracking(),
                pair => pair.assignment.ModuleId,
                module => module.Id,
                (pair, module) => new
                {
                    pair.assignment.OrganizationId,
                    OrganizationName = pair.organization.Name,
                    ModuleKey = module.ModuleKey,
                    Label = module.DisplayName,
                    Description = module.Description,
                    ShortLabel = module.ShortLabel,
                    pair.assignment.SortOrder,
                })
            .OrderBy(x => x.OrganizationName)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Label)
            .ToListAsync(cancellationToken);

        var modulesByOrganization = modulesRows
            .GroupBy(x => new { x.OrganizationId, x.OrganizationName })
            .ToDictionary(
                group => group.Key.OrganizationId,
                group => group.Select(x => new AdminUserModuleAssignmentResponse(
                    x.ModuleKey,
                    x.Label,
                    x.Description,
                    x.ShortLabel,
                    x.SortOrder)).ToArray());

        var organizationModules = membershipRows
            .Select(membership => new AdminUserOrganizationModulesResponse(
                membership.OrganizationId,
                membership.OrganizationName,
                modulesByOrganization.TryGetValue(membership.OrganizationId, out var modules)
                    ? modules
                    : []))
            .ToArray();

        return new AdminUserResponse(
            user.Id,
            user.Email!,
            user.DisplayName,
            user.Status,
            [.. roles.Order()],
            organizationModules);
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

    private static async Task<IResult?> ValidateOrganizationModulesAsync(
        IReadOnlyList<AdminUserOrganizationModulesRequest>? organizationModules,
        IReadOnlyCollection<string> roles,
        AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var requested = organizationModules ?? [];

        if (roles.Contains(IdentityRoles.Sysadmin) && requested.Count > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["organizationModules"] =
                [
                    "Platform sysadmins cannot have organization assignments."
                ],
            });
        }

        if (requested.Count > 1)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["organizationModules"] =
                [
                    "A user can be assigned to only one organization."
                ],
            });
        }

        var duplicateOrganizations = requested
            .GroupBy(x => x.OrganizationId)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToArray();
        if (duplicateOrganizations.Length > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["organizationModules"] =
                [
                    $"Duplicate organization entries are not allowed: {string.Join(", ", duplicateOrganizations)}"
                ],
            });
        }

        var requestedModuleKeys = requested
            .SelectMany(x => NormalizeModuleKeys(x.OrderedModuleKeys ?? []))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var organizationIds = requested
            .Select(x => x.OrganizationId)
            .Distinct()
            .ToArray();

        if (organizationIds.Length == 0)
        {
            return null;
        }

        var existingOrganizationIds = await db.Tenants
            .AsNoTracking()
            .Where(x => organizationIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var missingOrganizationIds = organizationIds
            .Except(existingOrganizationIds)
            .ToArray();
        if (missingOrganizationIds.Length > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["organizationModules"] =
                [
                    $"Unknown organizations: {string.Join(", ", missingOrganizationIds)}"
                ],
            });
        }

        var existingModules = await db.SystemModules
            .AsNoTracking()
            .Where(x => x.IsActive && requestedModuleKeys.Contains(x.ModuleKey))
            .Select(x => x.ModuleKey)
            .ToListAsync(cancellationToken);
        var unknownModuleKeys = requestedModuleKeys
            .Except(existingModules, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknownModuleKeys.Length > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["organizationModules"] =
                [
                    $"Unsupported module keys: {string.Join(", ", unknownModuleKeys)}"
                ],
            });
        }

        var allowedByOrganization = await db.OrganizationModules
            .AsNoTracking()
            .Where(x => organizationIds.Contains(x.OrganizationId))
            .Join(
                db.SystemModules.AsNoTracking(),
                assignment => assignment.ModuleId,
                module => module.Id,
                (assignment, module) => new { assignment.OrganizationId, module.ModuleKey })
            .ToListAsync(cancellationToken);

        var allowedLookup = allowedByOrganization
            .GroupBy(x => x.OrganizationId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.ModuleKey).ToHashSet(StringComparer.OrdinalIgnoreCase));

        foreach (var request in requested)
        {
            var requestedForOrganization = NormalizeModuleKeys(request.OrderedModuleKeys ?? []);
            if (requestedForOrganization.Count == 0)
            {
                continue;
            }

            var allowed = allowedLookup.TryGetValue(request.OrganizationId, out var keys)
                ? keys
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var disallowed = requestedForOrganization
                .Where(x => !allowed.Contains(x))
                .ToArray();
            if (disallowed.Length > 0)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["organizationModules"] =
                    [
                        $"Organization {request.OrganizationId} is missing assigned modules: {string.Join(", ", disallowed)}"
                    ],
                });
            }
        }

        return null;
    }

    private static async Task SaveOrganizationModulesAsync(
        Guid userId,
        IReadOnlyList<AdminUserOrganizationModulesRequest>? organizationModules,
        AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var requested = (organizationModules ?? [])
            .Take(1)
            .ToArray();

        var existing = await db.OrganizationUserModules
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            db.OrganizationUserModules.RemoveRange(existing);
        }

        var nowUtc = DateTime.UtcNow;
        var requestedOrganizationIds = new HashSet<Guid>();

        foreach (var group in requested)
        {
            requestedOrganizationIds.Add(group.OrganizationId);
            var orderedModuleKeys = NormalizeModuleKeys(group.OrderedModuleKeys ?? []);
            if (orderedModuleKeys.Count == 0)
            {
                continue;
            }

            var moduleRows = await db.OrganizationModules
                .AsNoTracking()
                .Where(x => x.OrganizationId == group.OrganizationId)
                .Join(
                    db.SystemModules.AsNoTracking(),
                    assignment => assignment.ModuleId,
                    module => module.Id,
                    (assignment, module) => new { assignment.ModuleId, module.ModuleKey })
                .Where(x => orderedModuleKeys.Contains(x.ModuleKey))
                .ToListAsync(cancellationToken);
            var moduleIdByKey = moduleRows
                .ToDictionary(x => x.ModuleKey, x => x.ModuleId, StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < orderedModuleKeys.Count; index++)
            {
                if (!moduleIdByKey.TryGetValue(orderedModuleKeys[index], out var moduleId))
                {
                    continue;
                }

                db.OrganizationUserModules.Add(new OrganizationUserModuleData
                {
                    OrganizationId = group.OrganizationId,
                    UserId = userId,
                    ModuleId = moduleId,
                    SortOrder = index,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                });
            }
        }

        if (requestedOrganizationIds.Count > 0)
        {
            var existingMemberships = await db.TenantMemberships
                .Where(x => x.UserId == userId)
                .ToListAsync(cancellationToken);

            var staleMemberships = existingMemberships
                .Where(x => !requestedOrganizationIds.Contains(x.TenantId))
                .ToArray();
            if (staleMemberships.Length > 0)
            {
                db.TenantMemberships.RemoveRange(staleMemberships);
            }

            var existingMembershipOrganizationIds = existingMemberships
                .Select(x => x.TenantId)
                .ToHashSet();

            foreach (var organizationId in requestedOrganizationIds)
            {
                if (existingMembershipOrganizationIds.Contains(organizationId))
                {
                    continue;
                }

                db.TenantMemberships.Add(new TenantMembershipData
                {
                    TenantId = organizationId,
                    UserId = userId,
                    Role = TenantRole.Member.ToString(),
                    CreatedAtUtc = nowUtc,
                });
            }
        }
        else
        {
            var memberships = await db.TenantMemberships
                .Where(x => x.UserId == userId)
                .ToListAsync(cancellationToken);
            if (memberships.Count > 0)
            {
                db.TenantMemberships.RemoveRange(memberships);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static List<string> NormalizeRoles(IReadOnlyList<string> roles) =>
        roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToList();

    private static List<string> NormalizeModuleKeys(IReadOnlyList<string> moduleKeys)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var moduleKey in moduleKeys)
        {
            if (string.IsNullOrWhiteSpace(moduleKey))
            {
                continue;
            }

            var trimmed = moduleKey.Trim().ToLowerInvariant();
            if (!seen.Add(trimmed))
            {
                continue;
            }

            normalized.Add(trimmed);
        }

        return normalized;
    }

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
    IReadOnlyList<string> Roles,
    IReadOnlyList<AdminUserOrganizationModulesRequest>? OrganizationModules);

public sealed record UpdateAdminUserRequest(
    string Email,
    string? DisplayName,
    UserStatus Status,
    IReadOnlyList<string> Roles,
    IReadOnlyList<AdminUserOrganizationModulesRequest>? OrganizationModules);

public sealed record SetUserStatusRequest(UserStatus Status);

public sealed record AdminUserOrganizationModulesRequest(
    Guid OrganizationId,
    IReadOnlyList<string>? OrderedModuleKeys);

public sealed record AdminUserModuleAssignmentResponse(
    string Key,
    string Label,
    string Description,
    string ShortLabel,
    int SortOrder);

public sealed record AdminUserOrganizationModulesResponse(
    Guid OrganizationId,
    string OrganizationName,
    IReadOnlyList<AdminUserModuleAssignmentResponse> Modules);

public sealed record AdminUserResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    UserStatus Status,
    string[] Roles,
    AdminUserOrganizationModulesResponse[] OrganizationModules);
