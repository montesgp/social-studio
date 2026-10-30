using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Domain.Tenants;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.ApiService.Endpoints.Common;

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

        group.MapGet("/workspace-modules", GetWorkspaceModules)
            .WithName("GetMyWorkspaceModules")
            .WithSummary("Returns available workspace modules for the current user and organization context")
            .Produces<IReadOnlyList<WorkspaceModuleResponse>>(StatusCodes.Status200OK)
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

        var assignedOrganizationIds = await db.OrganizationUserModules
            .AsNoTracking()
            .Where(x => x.UserId == userId.Value)
            .Select(x => x.OrganizationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (assignedOrganizationIds.Count > 0)
        {
            var existingMembershipIds = await db.TenantMemberships
                .AsNoTracking()
                .Where(x => x.UserId == userId.Value && assignedOrganizationIds.Contains(x.TenantId))
                .Select(x => x.TenantId)
                .ToListAsync(cancellationToken);

            var missingMemberships = assignedOrganizationIds
                .Except(existingMembershipIds)
                .ToArray();

            if (missingMemberships.Length > 0)
            {
                var nowUtc = DateTime.UtcNow;
                foreach (var organizationId in missingMemberships)
                {
                    db.TenantMemberships.Add(new TenantMembershipData
                    {
                        TenantId = organizationId,
                        UserId = userId.Value,
                        Role = TenantRole.Member.ToString(),
                        CreatedAtUtc = nowUtc,
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var orgs = await db.TenantMemberships
            .Where(m => m.UserId == userId)
            .Where(m => assignedOrganizationIds.Count == 0 || assignedOrganizationIds.Contains(m.TenantId))
            .Select(m => m.Tenant!)
            .Where(t => t != null && t.IsActive)
            .Select(t => new MyOrganizationResponse(t.Id, t.Name))
            .ToListAsync(cancellationToken);

        return Results.Ok(orgs);
    }

    private static async Task<IResult> GetWorkspaceModules(
        [FromServices] ICurrentUser currentUser,
        [FromServices] ICurrentTenant currentTenant,
        [FromServices] AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        if (currentUser.IsPlatformAdmin)
        {
            var platformModules = await db.SystemModules
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.CreatedAtUtc)
                .ThenBy(x => x.ModuleKey)
                .Select(x => new WorkspaceModuleResponse(
                    x.Id,
                    x.ModuleKey,
                    x.DisplayName,
                    x.Description,
                    x.ShortLabel))
                .ToListAsync(cancellationToken);
            return Results.Ok(platformModules);
        }

        var organizationId = currentTenant.TenantId?.Value;
        if (organizationId is null)
        {
            return Results.Ok(Array.Empty<WorkspaceModuleResponse>());
        }

        var assignedModules = await db.OrganizationUserModules
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.UserId == userId.Value && x.Module != null && x.Module.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAtUtc)
            .Select(x => new WorkspaceModuleResponse(
                x.ModuleId,
                x.Module!.ModuleKey,
                x.Module.DisplayName,
                x.Module.Description,
                x.Module.ShortLabel))
            .ToListAsync(cancellationToken);

        if (assignedModules.Count == 0)
        {
            return Results.Ok(Array.Empty<WorkspaceModuleResponse>());
        }

        var organizationModuleIds = await db.OrganizationModules
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.ModuleId)
            .ToHashSetAsync(cancellationToken);

        var filtered = assignedModules
            .Where(x => organizationModuleIds.Contains(x.Id))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        return Results.Ok(filtered);
    }
}

public sealed record MeResponse(Guid? UserId, string? Email, bool IsPlatformAdmin);

public sealed record MyOrganizationResponse(Guid Id, string Name);

public sealed record WorkspaceModuleResponse(
    Guid Id,
    string Key,
    string Label,
    string Description,
    string ShortLabel);
