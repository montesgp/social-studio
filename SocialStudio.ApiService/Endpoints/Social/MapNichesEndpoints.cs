using SocialStudio.ApiService.Endpoints.Common;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Common.Pagination;
using SocialStudio.Application.Features.Social.Clients.Niches;
using SocialStudio.Application.Features.Social.Clients.Niches.Assign;
using SocialStudio.Application.Features.Social.Clients.Niches.Get;
using SocialStudio.Application.Features.Social.Niches;
using SocialStudio.Application.Features.Social.Niches.Activate;
using SocialStudio.Application.Features.Social.Niches.Create;
using SocialStudio.Application.Features.Social.Niches.Deactivate;
using SocialStudio.Application.Features.Social.Niches.GetAll;
using SocialStudio.Application.Features.Social.Niches.GetById;
using SocialStudio.Application.Features.Social.Niches.Update;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace SocialStudio.ApiService.Endpoints.Social;

public static class NichesEndpointsExtensions
{
    public static IEndpointRouteBuilder MapNichesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/niches").WithTags("Social Niches");

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin)
            .WithName("CreateNiche")
            .WithSummary("Create a niche/sector catalog entry")
            .Produces<NicheResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", ListAsync)
            .RequireAuthorization()
            .WithName("ListNiches")
            .WithSummary("Search the niche catalog (bounded page; no search term returns the newest entries)")
            .Produces<PageResult<NicheResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .RequireAuthorization()
            .WithName("GetNicheById")
            .WithSummary("Get a niche by id (inactive returns 404 for non-admin)")
            .Produces<NicheResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin)
            .WithName("UpdateNiche")
            .WithSummary("Update displayName, iconKey and curated hashtags (Code is immutable)")
            .Produces<NicheResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin)
            .WithName("DeactivateNiche")
            .WithSummary("Deactivate (soft-delete) a niche")
            .Produces<NicheResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .RequireAuthorization(IdentityServicesExtensions.Policies.PlatformAdmin)
            .WithName("ActivateNiche")
            .WithSummary("Reactivate a deactivated niche")
            .Produces<NicheResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var clientGroup = app.MapGroup("/api/social/clients/{clientId:guid}/niches")
            .WithTags("Social Clients")
            .RequireAuthorization();

        clientGroup.MapGet("/", GetClientNichesAsync)
            .WithName("GetClientNiches")
            .WithSummary("Get the client's current niche selection")
            .Produces<ClientNichesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        clientGroup.MapPut("/", AssignClientNichesAsync)
            .WithName("AssignClientNiches")
            .WithSummary("Replace the client's niche selection with catalog entries")
            .Produces<ClientNichesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateNicheRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CreateNicheCommand(body.Code, body.DisplayName, body.IconKey, body.SearchHashtags ?? []),
            cancellationToken);

        return result.Match(niche => TypedResults.Created($"/api/social/niches/{niche.Id}", niche));
    }

    private static async Task<IResult> ListAsync(
        ICurrentUser currentUser,
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] bool? includeInactive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetAllNichesQuery.DefaultPageSize,
        [FromQuery] string? searchTerm = null)
    {
        // Non-admin callers cannot see inactive niches; the flag is silently coerced to false.
        var effectiveIncludeInactive = currentUser.IsPlatformAdmin && (includeInactive ?? false);

        var result = await sender.Send(
            new GetAllNichesQuery(effectiveIncludeInactive, page, pageSize, searchTerm),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ICurrentUser currentUser,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetNicheByIdQuery(id), cancellationToken);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        if (!result.Value.IsActive && !currentUser.IsPlatformAdmin)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(result.Value);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateNicheRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateNicheCommand(id, body.DisplayName, body.IconKey, body.SearchHashtags ?? []),
            cancellationToken);

        return result.Match(niche => (IResult)TypedResults.Ok(niche));
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeactivateNicheCommand(id), cancellationToken);

        return result.Match(niche => (IResult)TypedResults.Ok(niche));
    }

    private static async Task<IResult> ActivateAsync(
        Guid id,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ActivateNicheCommand(id), cancellationToken);

        return result.Match(niche => (IResult)TypedResults.Ok(niche));
    }

    private static async Task<IResult> GetClientNichesAsync(
        Guid clientId,
        ISender sender,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!TryResolveOrganization(currentTenant, currentUser, out var organizationId, out var failure))
        {
            return failure!;
        }

        var result = await sender.Send(
            new GetClientNichesQuery(clientId, organizationId),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> AssignClientNichesAsync(
        Guid clientId,
        [FromBody] AssignClientNichesRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!TryResolveOrganization(currentTenant, currentUser, out var organizationId, out var failure))
        {
            return failure!;
        }

        var result = await sender.Send(
            new AssignClientNichesCommand(clientId, organizationId, body.NicheIds ?? []),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    /// <summary>
    /// Mirrors the client by-id endpoints: a platform admin resolves across every organization,
    /// everyone else is pinned to their active tenant.
    /// </summary>
    private static bool TryResolveOrganization(
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        out Guid? organizationId,
        out IResult? failure)
    {
        if (currentUser.IsPlatformAdmin)
        {
            organizationId = null;
            failure = null;
            return true;
        }

        if (currentTenant.TenantId is not { } tenantId)
        {
            organizationId = null;
            failure = ClientErrors.OrganizationRequired.ToProblem();
            return false;
        }

        organizationId = tenantId.Value;
        failure = null;
        return true;
    }
}

public sealed record CreateNicheRequest(
    string Code,
    string DisplayName,
    string? IconKey,
    IReadOnlyList<string>? SearchHashtags);

public sealed record UpdateNicheRequest(
    string DisplayName,
    string? IconKey,
    IReadOnlyList<string>? SearchHashtags);

public sealed record AssignClientNichesRequest(IReadOnlyList<Guid>? NicheIds);
