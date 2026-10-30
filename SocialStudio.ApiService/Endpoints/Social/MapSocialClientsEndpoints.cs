using SocialStudio.ApiService.Endpoints.Common;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Common.Pagination;
using SocialStudio.Application.Features.Social.Clients;
using SocialStudio.Application.Features.Social.Clients.Activate;
using SocialStudio.Application.Features.Social.Clients.Create;
using SocialStudio.Application.Features.Social.Clients.Delete;
using SocialStudio.Application.Features.Social.Clients.Deactivate;
using SocialStudio.Application.Features.Social.Clients.GetAll;
using SocialStudio.Application.Features.Social.Clients.GetById;
using SocialStudio.Application.Features.Social.Clients.Update;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace SocialStudio.ApiService.Endpoints.Social;

public static class SocialClientsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapSocialClientsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients")
            .WithTags("Social Clients")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListSocialClients")
            .WithSummary("List Social Studio clients for the resolved organization")
            .Produces<PageResult<SocialClientResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetSocialClientById")
            .WithSummary("Get a Social Studio client by id within the resolved organization")
            .Produces<SocialClientResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateSocialClient")
            .WithSummary("Create a Social Studio client for the resolved organization")
            .Produces<SocialClientResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateSocialClient")
            .WithSummary("Rename/update a Social Studio client within the resolved organization")
            .Produces<SocialClientResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivateSocialClient")
            .WithSummary("Archive (soft-delete) a Social Studio client")
            .Produces<SocialClientResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("ActivateSocialClient")
            .WithSummary("Reactivate an archived Social Studio client")
            .Produces<SocialClientResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteSocialClient")
            .WithSummary("Hard-delete a Social Studio client and all client-scoped data")
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin)
            .Produces<DeletedSocialClientResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

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
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        Guid? resolvedOrganizationId;

        // Mirrors ListAsync: a platform admin lists clients across every organization, so the
        // by-id lookup has to reach the same rows. Scoping it to the admin's own active tenant
        // made every client outside that tenant 404 straight out of the list.
        if (currentUser.IsPlatformAdmin)
        {
            resolvedOrganizationId = null;
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
            new GetSocialClientByIdQuery(id, resolvedOrganizationId),
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
            new CreateSocialClientCommand(
                organizationId.Value,
                body.Name,
                body.Description,
                body.SectorOrNiche,
                body.BackgroundSourcingPolicy,
                body.AestheticRefreshThresholdDays,
                body.MaxPiecesPerGeneration,
                body.SignsOwnSlides),
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
            new UpdateSocialClientCommand(
                id,
                organizationId.Value,
                body.Name,
                body.Description,
                body.SectorOrNiche,
                body.BackgroundSourcingPolicy,
                body.AestheticRefreshThresholdDays,
                body.SignsOwnSlides,
                body.MaxPiecesPerGeneration),
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

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DeleteSocialClientCommand(id),
            cancellationToken);

        return result.Match(client => (IResult)TypedResults.Ok(client));
    }
}

public sealed record CreateSocialClientRequest(
    string Name,
    string? Description,
    string? SectorOrNiche = null,
    // WU-8 (BSP-08): optional background sourcing policy. Null = Unset = legacy 3-tier default.
    Domain.Social.Clients.BackgroundSourcingPolicy? BackgroundSourcingPolicy = null,
    // WU-7 (CAG-CS-02): optional aesthetic refresh threshold in days. Null = system default (30 days).
    int? AestheticRefreshThresholdDays = null,
    // Issue #70: optional opt-in for @handle signature on generated slides. Plain boolean,
    // default false = not signed.
    bool SignsOwnSlides = false,
    // Per-client cap on pieces built per generation job. Null = no cap.
    int? MaxPiecesPerGeneration = null);

public sealed record UpdateSocialClientRequest(
    string Name,
    string? Description,
    string? SectorOrNiche = null,
    // WU-8 (BSP-08b): optional policy update. Null = leave current policy unchanged.
    Domain.Social.Clients.BackgroundSourcingPolicy? BackgroundSourcingPolicy = null,
    // WU-7 (CAG-CS-02): optional threshold update in days. Null = leave current threshold unchanged.
    int? AestheticRefreshThresholdDays = null,
    // Issue #70: always-explicit opt-in for @handle signature on generated slides. Plain boolean.
    bool SignsOwnSlides = false,
    // Per-client generation piece cap. Null = leave the current cap unchanged.
    int? MaxPiecesPerGeneration = null);
