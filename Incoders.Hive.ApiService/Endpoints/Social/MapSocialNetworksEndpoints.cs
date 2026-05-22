using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Application.Features.Social.Networks;
using Incoders.Hive.Application.Features.Social.Networks.Activate;
using Incoders.Hive.Application.Features.Social.Networks.Create;
using Incoders.Hive.Application.Features.Social.Networks.Deactivate;
using Incoders.Hive.Application.Features.Social.Networks.GetAll;
using Incoders.Hive.Application.Features.Social.Networks.GetById;
using Incoders.Hive.Application.Features.Social.Networks.Update;
using Incoders.Hive.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class SocialNetworksEndpointsExtensions
{
    public static IEndpointRouteBuilder MapSocialNetworksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/networks").WithTags("Social Networks");

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin)
            .WithName("CreateSocialNetwork")
            .WithSummary("Create a social network catalog entry")
            .Produces<SocialNetworkResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", ListAsync)
            .RequireAuthorization()
            .WithName("ListSocialNetworks")
            .WithSummary("List paginated social networks (non-admin callers only see active)")
            .Produces<PageResult<SocialNetworkResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .RequireAuthorization()
            .WithName("GetSocialNetworkById")
            .WithSummary("Get a social network by id (inactive returns 404 for non-admin)")
            .Produces<SocialNetworkResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin)
            .WithName("UpdateSocialNetwork")
            .WithSummary("Update displayName and iconKey (Code is immutable)")
            .Produces<SocialNetworkResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin)
            .WithName("DeactivateSocialNetwork")
            .WithSummary("Deactivate (soft-delete) a social network")
            .Produces<SocialNetworkResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin)
            .WithName("ActivateSocialNetwork")
            .WithSummary("Reactivate a deactivated social network")
            .Produces<SocialNetworkResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateSocialNetworkRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateSocialNetworkCommand(body.Code, body.DisplayName, body.IconKey),
            cancellationToken);

        return result.Match(network => TypedResults.Created($"/api/social/networks/{network.Id}", network));
    }

    private static async Task<IResult> ListAsync(
        ICurrentUser currentUser,
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] bool? includeInactive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? searchTerm = null)
    {
        // ADR 5: non-admin callers cannot see inactive networks; flag silently coerced to false
        var effectiveIncludeInactive = currentUser.IsPlatformAdmin && (includeInactive ?? false);

        var result = await sender.Send(
            new GetAllSocialNetworksQuery(effectiveIncludeInactive, page, pageSize, searchTerm),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ICurrentUser currentUser,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new GetSocialNetworkByIdQuery(id),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        // Resolution 2: endpoint returns 404 to non-admin for inactive networks
        if (!result.Value.IsActive && !currentUser.IsPlatformAdmin)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateSocialNetworkRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateSocialNetworkCommand(id, body.DisplayName, body.IconKey),
            cancellationToken);

        return result.Match(network => (IResult)TypedResults.Ok(network));
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DeactivateSocialNetworkCommand(id),
            cancellationToken);

        return result.Match(network => (IResult)TypedResults.Ok(network));
    }

    private static async Task<IResult> ActivateAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ActivateSocialNetworkCommand(id),
            cancellationToken);

        return result.Match(network => (IResult)TypedResults.Ok(network));
    }
}

public sealed record CreateSocialNetworkRequest(string Code, string DisplayName, string IconKey);

public sealed record UpdateSocialNetworkRequest(string DisplayName, string IconKey);
