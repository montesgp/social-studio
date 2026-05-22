using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Application.Features.Social.Clients;
using Incoders.Hive.Application.Features.Social.Clients.Activate;
using Incoders.Hive.Application.Features.Social.Clients.Create;
using Incoders.Hive.Application.Features.Social.Clients.Deactivate;
using Incoders.Hive.Application.Features.Social.Clients.GetAll;
using Incoders.Hive.Application.Features.Social.Clients.GetById;
using Incoders.Hive.Application.Features.Social.Clients.Update;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class SocialClientsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapSocialClientsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients")
            .WithTags("Social Clients")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListSocialClients")
            .WithSummary("List Hive Social clients for the resolved organization")
            .Produces<PageResult<SocialClientResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetSocialClientById")
            .WithSummary("Get a Hive Social client by id within the resolved organization")
            .Produces<SocialClientResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateSocialClient")
            .WithSummary("Create a Hive Social client for the resolved organization")
            .Produces<SocialClientResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateSocialClient")
            .WithSummary("Rename/update a Hive Social client within the resolved organization")
            .Produces<SocialClientResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivateSocialClient")
            .WithSummary("Archive (soft-delete) a Hive Social client")
            .Produces<SocialClientResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("ActivateSocialClient")
            .WithSummary("Reactivate an archived Hive Social client")
            .Produces<SocialClientResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ListAsync(
        ISender sender,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        [FromQuery] Guid? organizationId = null,
        [FromQuery] string? name = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        Guid? resolvedOrganizationId;

        if (currentUser.IsPlatformAdmin)
        {
            // null = all orgs; explicit param scopes to a specific org
            resolvedOrganizationId = organizationId;
        }
        else
        {
            if (currentTenant.TenantId is not { } tenantId)
            {
                return ClientErrors.OrganizationRequired.ToProblem();
            }
            resolvedOrganizationId = tenantId.Value;
        }

        var result = await sender.Send(
            new GetSocialClientsQuery(resolvedOrganizationId, name, includeInactive, page, pageSize),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new GetSocialClientByIdQuery(id, organizationId.Value),
            cancellationToken);

        return result.Match(client => (IResult)TypedResults.Ok(client));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateSocialClientRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new CreateSocialClientCommand(organizationId.Value, body.Name, body.Description, body.SectorOrNiche),
            cancellationToken);

        return result.Match(client => TypedResults.Created($"/api/social/clients/{client.Id}", client));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateSocialClientRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new UpdateSocialClientCommand(id, organizationId.Value, body.Name, body.Description, body.SectorOrNiche),
            cancellationToken);

        return result.Match(client => (IResult)TypedResults.Ok(client));
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new DeactivateSocialClientCommand(id, organizationId.Value),
            cancellationToken);

        return result.Match(client => (IResult)TypedResults.Ok(client));
    }

    private static async Task<IResult> ActivateAsync(
        Guid id,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new ActivateSocialClientCommand(id, organizationId.Value),
            cancellationToken);

        return result.Match(client => (IResult)TypedResults.Ok(client));
    }
}

public sealed record CreateSocialClientRequest(string Name, string? Description, string? SectorOrNiche = null);

public sealed record UpdateSocialClientRequest(string Name, string? Description, string? SectorOrNiche = null);
