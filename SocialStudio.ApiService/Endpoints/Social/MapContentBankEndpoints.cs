using SocialStudio.ApiService.Endpoints.Common;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Features.Social.Publications;
using SocialStudio.Application.Features.Social.Publications.EnsureClientContentMaterialized;
using SocialStudio.Application.Features.Social.Publications.ListClientContentBank;
using SocialStudio.Application.Features.Social.Publications.PublishNow;
using SocialStudio.Application.Features.Social.Publications.Schedule;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace SocialStudio.ApiService.Endpoints.Social;

/// <summary>
/// Client-wide Content Bank endpoints (design §9): the read seam over the whole Social/ContentStudio
/// context for a client, plus the item-level publish/schedule actions that create a
/// <c>Publication</c>. Org-scoped via <see cref="ICurrentTenant.TenantId"/>.
/// </summary>
public static class ContentBankEndpointsExtensions
{
    public static IEndpointRouteBuilder MapContentBankEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/content-bank")
            .WithTags("Social Content Bank")
            .RequireAuthorization();

        group.MapGet("/clients/{clientId:guid}/items", ListItemsAsync)
            .WithName("ListContentBankItems")
            .WithSummary("Client-wide Content Bank listing; runs lazy materialization first, returns items + publication summaries")
            .Produces<IReadOnlyList<ContentBankItemResponse>>(StatusCodes.Status200OK);

        group.MapPost("/items/{itemId:guid}/publish", PublishNowAsync)
            .WithName("PublishContentBankItemNow")
            .WithSummary("Publish an item immediately to a destination (editorial gate enforced)")
            .Produces<PublicationResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/items/{itemId:guid}/schedule", ScheduleAsync)
            .WithName("ScheduleContentBankItem")
            .WithSummary("Create a Scheduled publication for an item (no v1 dispatcher fires it)")
            .Produces<PublicationResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ListItemsAsync(
        Guid clientId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? state,
        ICurrentTenant tenant,
        ISender sender,
        CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        // Lazy materialization (design §5.3) runs BEFORE the listing query so "all content for a
        // client" holds without a background worker.
        await sender.Send(new EnsureClientContentMaterializedCommand(org.Value, clientId), ct);

        var result = await sender.Send(
            new ListClientContentBankQuery(org.Value, clientId, fromUtc, toUtc, state), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> PublishNowAsync(
        Guid itemId,
        [FromBody] PublishItemNowRequest body,
        ICurrentTenant tenant,
        ISender sender,
        CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(
            new PublishItemNowCommand(
                org.Value, body.ClientId, itemId, body.ClientAccountConnectionId, body.SocialNetworkId,
                body.NetworkKey),
            ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> ScheduleAsync(
        Guid itemId,
        [FromBody] SchedulePublicationRequest body,
        ICurrentTenant tenant,
        ISender sender,
        CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(
            new SchedulePublicationCommand(
                org.Value, body.ClientId, itemId, body.ClientAccountConnectionId, body.SocialNetworkId,
                body.NetworkKey, body.ScheduledForUtc),
            ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }
}

public sealed record PublishItemNowRequest(
    Guid ClientId, Guid ClientAccountConnectionId, Guid SocialNetworkId, string NetworkKey);

public sealed record SchedulePublicationRequest(
    Guid ClientId, Guid ClientAccountConnectionId, Guid SocialNetworkId, string NetworkKey, DateTime ScheduledForUtc);
