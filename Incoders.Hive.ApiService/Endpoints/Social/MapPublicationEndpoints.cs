using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.Publications;
using Incoders.Hive.Application.Features.Social.Publications.Cancel;
using Incoders.Hive.Application.Features.Social.Publications.GetPublication;
using Incoders.Hive.Application.Features.Social.Publications.Reclaim;
using Incoders.Hive.Application.Features.Social.Publications.Retry;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Social;

/// <summary>
/// Publication lifecycle endpoints (design §9): read a single publication, and manually drive its
/// terminal-recovery actions (retry / cancel / reclaim). Org-scoped via
/// <see cref="ICurrentTenant.TenantId"/>.
/// </summary>
public static class PublicationEndpointsExtensions
{
    public static IEndpointRouteBuilder MapPublicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/publications")
            .WithTags("Social Publications")
            .RequireAuthorization();

        group.MapGet("/{publicationId:guid}", GetAsync)
            .WithName("GetPublication")
            .Produces<PublicationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{publicationId:guid}/retry", RetryAsync)
            .WithName("RetryPublication")
            .WithSummary("Manually retry a PublishFailed publication")
            .Produces<PublicationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{publicationId:guid}/cancel", CancelAsync)
            .WithName("CancelPublication")
            .WithSummary("Cancel a Scheduled or PublishFailed publication")
            .Produces<PublicationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{publicationId:guid}/reclaim", ReclaimAsync)
            .WithName("ReclaimStuckPublication")
            .WithSummary("Recover a publication stuck in Publishing (crash mid-publish) into PublishFailed")
            .Produces<PublicationResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid publicationId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new GetPublicationQuery(org.Value, publicationId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> RetryAsync(
        Guid publicationId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new RetryPublicationCommand(org.Value, publicationId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> CancelAsync(
        Guid publicationId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new CancelPublicationCommand(org.Value, publicationId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> ReclaimAsync(
        Guid publicationId,
        [FromBody] ReclaimPublicationRequest body,
        ICurrentTenant tenant,
        ISender sender,
        CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(
            new ReclaimStuckPublicationCommand(org.Value, publicationId, body.Reason), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }
}

public sealed record ReclaimPublicationRequest(string Reason);
