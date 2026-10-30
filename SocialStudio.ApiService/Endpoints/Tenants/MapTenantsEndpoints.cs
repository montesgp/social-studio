using SocialStudio.ApiService.Endpoints.Common;
using SocialStudio.Application.Common.Pagination;
using SocialStudio.Application.Features.Tenants;
using SocialStudio.Infrastructure.Identity;
using SocialStudio.Infrastructure.Persistence.Postgres.Entities;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace SocialStudio.ApiService.Endpoints.Tenants;

/// <summary>
/// Organization / tenant management endpoints. Available when multi-tenancy is enabled.
/// </summary>
public static class TenantsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapTenantsEndpoints(this IEndpointRouteBuilder app)
    {
        MapOrganizationRoutes(app.MapGroup("/api/organizations").WithTags("Organizations"), "Organization");
        MapOrganizationRoutes(app.MapGroup("/api/tenants").WithTags("Tenants"), "Tenant");

        return app;
    }

    private static void MapOrganizationRoutes(RouteGroupBuilder group, string routeLabel)
    {
        group.RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin);

        group.MapGet("/", ListAsync)
            .WithName($"List{routeLabel}s")
            .WithSummary("List organizations (tenant-backed)")
            .Produces<PageResult<TenantResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName($"Get{routeLabel}ById")
            .WithSummary("Get an organization by id")
            .Produces<TenantResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName($"Create{routeLabel}")
            .WithSummary("Create an organization; the current user is added as Owner")
            .Produces<TenantResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName($"Update{routeLabel}")
            .WithSummary("Rename/update organization")
            .Produces<TenantResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName($"Deactivate{routeLabel}")
            .WithSummary("Deactivate (archive) organization")
            .Produces<TenantResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName($"Activate{routeLabel}")
            .WithSummary("Reactivate an archived organization")
            .Produces<TenantResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName($"Delete{routeLabel}")
            .WithSummary("Permanently delete an organization and all organization-scoped data")
            .Produces<DeletedTenantResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/modules", GetModulesAsync)
            .WithName($"Get{routeLabel}Modules")
            .WithSummary("Get system modules and organization module assignments")
            .Produces<OrganizationModulesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/modules", UpsertModulesAsync)
            .WithName($"Upsert{routeLabel}Modules")
            .WithSummary("Assign and order modules for an organization")
            .Produces<OrganizationModulesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();
    }

    private static async Task<IResult> ListAsync(
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] string? name = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await sender.Send(new GetTenantsQuery(name, includeInactive, page, pageSize), cancellationToken);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateTenantRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateTenantCommand(body.Name), cancellationToken);
        return result.Match(tenant => TypedResults.Created($"/api/organizations/{tenant.Id}", tenant));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetTenantByIdQuery(id), cancellationToken);
        return result.Match(tenant => (IResult)TypedResults.Ok(tenant));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateTenantRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateTenantCommand(id, body.Name), cancellationToken);
        return result.Match(tenant => (IResult)TypedResults.Ok(tenant));
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeactivateTenantCommand(id), cancellationToken);
        return result.Match(tenant => (IResult)TypedResults.Ok(tenant));
    }

    private static async Task<IResult> ActivateAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ActivateTenantCommand(id), cancellationToken);
        return result.Match(tenant => (IResult)TypedResults.Ok(tenant));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteTenantCommand(id), cancellationToken);
        return result.Match(deleted => (IResult)TypedResults.Ok(deleted));
    }

    private static async Task<IResult> GetModulesAsync(
        Guid id,
        [FromServices] AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var exists = await db.Tenants
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);
        if (!exists)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Organization not found");
        }

        var response = await BuildOrganizationModulesResponseAsync(id, db, cancellationToken);
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> UpsertModulesAsync(
        Guid id,
        [FromBody] UpsertOrganizationModulesRequest body,
        [FromServices] AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var exists = await db.Tenants
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);
        if (!exists)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Organization not found");
        }

        var normalizedKeys = (body.OrderedModuleKeys ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var availableModules = await db.SystemModules
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);
        var modulesByKey = availableModules
            .ToDictionary(x => x.ModuleKey, x => x, StringComparer.OrdinalIgnoreCase);

        var unknownKeys = normalizedKeys
            .Where(x => !modulesByKey.ContainsKey(x))
            .ToArray();
        if (unknownKeys.Length > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["orderedModuleKeys"] = [$"Unsupported module keys: {string.Join(", ", unknownKeys)}"],
            });
        }

        var existing = await db.OrganizationModules
            .Where(x => x.OrganizationId == id)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            db.OrganizationModules.RemoveRange(existing);
        }

        var nowUtc = DateTime.UtcNow;
        for (var index = 0; index < normalizedKeys.Count; index++)
        {
            var module = modulesByKey[normalizedKeys[index]];
            db.OrganizationModules.Add(new OrganizationModuleData
            {
                OrganizationId = id,
                ModuleId = module.Id,
                SortOrder = index,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
            });
        }

        var assignedModuleIds = normalizedKeys
            .Select(x => modulesByKey[x].Id)
            .ToHashSet();

        var staleUserAssignments = await db.OrganizationUserModules
            .Where(x => x.OrganizationId == id && !assignedModuleIds.Contains(x.ModuleId))
            .ToListAsync(cancellationToken);
        if (staleUserAssignments.Count > 0)
        {
            db.OrganizationUserModules.RemoveRange(staleUserAssignments);
        }

        await db.SaveChangesAsync(cancellationToken);

        var response = await BuildOrganizationModulesResponseAsync(id, db, cancellationToken);
        return TypedResults.Ok(response);
    }

    private static async Task<OrganizationModulesResponse> BuildOrganizationModulesResponseAsync(
        Guid organizationId,
        AppIdentityDbContext db,
        CancellationToken cancellationToken)
    {
        var moduleRows = await db.SystemModules
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.ModuleKey,
                x.DisplayName,
                x.Description,
                x.ShortLabel,
                x.CreatedAtUtc,
                x.IsActive,
            })
            .ToListAsync(cancellationToken);

        var availableModules = moduleRows
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.ModuleKey)
            .Select(x => new SystemModuleResponse(
                x.Id,
                x.ModuleKey,
                x.DisplayName,
                x.Description,
                x.ShortLabel,
                x.CreatedAtUtc))
            .ToList();

        var moduleById = moduleRows.ToDictionary(x => x.Id);

        var assignedRows = await db.OrganizationModules
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new
            {
                x.ModuleId,
                x.SortOrder,
            })
            .ToListAsync(cancellationToken);

        var assignedModules = assignedRows
            .Select(x => moduleById.TryGetValue(x.ModuleId, out var module)
                ? new OrganizationModuleAssignmentResponse(
                    module.Id,
                    module.ModuleKey,
                    module.DisplayName,
                    module.Description,
                    module.ShortLabel,
                    x.SortOrder)
                : null)
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.ModuleKey)
            .ToList();

        return new OrganizationModulesResponse(organizationId, availableModules, assignedModules);
    }
}

public sealed record CreateTenantRequest(string Name);

public sealed record UpdateTenantRequest(string Name);

public sealed record UpsertOrganizationModulesRequest(IReadOnlyList<string>? OrderedModuleKeys);

public sealed record SystemModuleResponse(
    Guid Id,
    string ModuleKey,
    string DisplayName,
    string Description,
    string ShortLabel,
    DateTime CreatedAtUtc);

public sealed record OrganizationModuleAssignmentResponse(
    Guid ModuleId,
    string ModuleKey,
    string DisplayName,
    string Description,
    string ShortLabel,
    int SortOrder);

public sealed record OrganizationModulesResponse(
    Guid OrganizationId,
    IReadOnlyList<SystemModuleResponse> AvailableModules,
    IReadOnlyList<OrganizationModuleAssignmentResponse> AssignedModules);
