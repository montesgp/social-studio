using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Application.Features.Tenants;
using Incoders.Hive.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Tenants;

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
}

public sealed record CreateTenantRequest(string Name);

public sealed record UpdateTenantRequest(string Name);
