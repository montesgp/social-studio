using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ContentStudio;
using Incoders.Hive.Application.Features.Social.ContentStudio.Materialize;
using Incoders.Hive.Application.Features.Social.ContentStudio.Operations;
using Incoders.Hive.Application.Features.Social.ContentStudio.Queries;
using Incoders.Hive.Application.Features.Social.ContentStudio.Stage;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Social;

/// <summary>
/// Content Studio endpoints: materialize a generation run into a reviewable set, then review / edit /
/// approve / reject / discard / duplicate / delete / stage individual items. Organization-scoped.
/// </summary>
public static class ContentStudioEndpointsExtensions
{
    public static IEndpointRouteBuilder MapContentStudioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/content-studio")
            .WithTags("Social ContentStudio")
            .RequireAuthorization();

        group.MapPost("/runs/{jobId:guid}/materialize", MaterializeAsync)
            .WithName("MaterializeContentStudioRun")
            .WithSummary("Lazily (idempotently) materialize the Content Studio set for a completed generation run")
            .Produces<ContentSetResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/sets/{setId:guid}", GetSetAsync)
            .WithName("GetContentStudioSet")
            .Produces<ContentSetResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/sets/{setId:guid}/items", ListItemsAsync)
            .WithName("ListContentStudioItems")
            .Produces<IReadOnlyList<ContentItemResponse>>(StatusCodes.Status200OK);

        group.MapGet("/items/{itemId:guid}", GetItemAsync)
            .WithName("GetContentStudioItem")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/items/{itemId:guid}/preview", PreviewAsync)
            .WithName("GetContentStudioItemPreview")
            .Produces<ContentPreviewProjection>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/items/{itemId:guid}/text", EditTextAsync)
            .WithName("EditContentStudioItemText")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/items/{itemId:guid}/visual", EditVisualAsync)
            .WithName("EditContentStudioItemVisual")
            .WithSummary("Edit the visual draft (composition markup + asset references)")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/items/{itemId:guid}/variant", SpawnVariantAsync)
            .WithName("SpawnContentStudioVariant")
            .WithSummary("Create a variant of an item in the same variant group")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK);

        group.MapPost("/items/{itemId:guid}/approve", ApproveAsync)
            .WithName("ApproveContentStudioItem")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/items/{itemId:guid}/reject", RejectAsync)
            .WithName("RejectContentStudioItem")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/items/{itemId:guid}/discard", DiscardAsync)
            .WithName("DiscardContentStudioItem")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/items/{itemId:guid}/duplicate", DuplicateAsync)
            .WithName("DuplicateContentStudioItem")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK);

        group.MapDelete("/items/{itemId:guid}", DeleteAsync)
            .WithName("DeleteContentStudioItem")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/items/{itemId:guid}/stage", StageAsync)
            .WithName("StageContentStudioItem")
            .WithSummary("Stage an approved item for publication and emit the outbound handoff")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> MaterializeAsync(
        Guid jobId, [FromBody] MaterializeRunRequest body, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new MaterializeRunCommand(org.Value, body.ClientId, jobId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetSetAsync(
        Guid setId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new GetContentSetQuery(org.Value, setId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> ListItemsAsync(
        Guid setId, [FromQuery] string? state, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new ListContentItemsQuery(org.Value, setId, state), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetItemAsync(
        Guid itemId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new GetContentItemQuery(org.Value, itemId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> PreviewAsync(
        Guid itemId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new GetContentPreviewQuery(org.Value, itemId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> EditTextAsync(
        Guid itemId, [FromBody] EditTextRequest body, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new EditItemTextCommand(org.Value, itemId, body.Text), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> EditVisualAsync(
        Guid itemId, [FromBody] EditVisualRequest body, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(
            new EditItemVisualCommand(org.Value, itemId, body.VisualSpecMarkup, body.Assets ?? []),
            ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> SpawnVariantAsync(
        Guid itemId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new SpawnVariantCommand(org.Value, itemId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> ApproveAsync(
        Guid itemId, ICurrentTenant tenant, ICurrentUser currentUser, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org || currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new ApproveItemCommand(org.Value, itemId, userId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> RejectAsync(
        Guid itemId, [FromBody] RejectRequest body, ICurrentTenant tenant, ICurrentUser currentUser, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org || currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new RejectItemCommand(org.Value, itemId, userId, body.Reason), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> DiscardAsync(
        Guid itemId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new DiscardItemCommand(org.Value, itemId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> DuplicateAsync(
        Guid itemId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new DuplicateItemCommand(org.Value, itemId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> DeleteAsync(
        Guid itemId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new DeleteItemCommand(org.Value, itemId), ct);
        return result.Match(() => (IResult)TypedResults.NoContent());
    }

    private static async Task<IResult> StageAsync(
        Guid itemId, ICurrentTenant tenant, ICurrentUser currentUser, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org || currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new StageItemForPublicationCommand(org.Value, itemId, userId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }
}

public sealed record MaterializeRunRequest(Guid ClientId);

public sealed record EditTextRequest(string Text);

public sealed record RejectRequest(string Reason);

public sealed record EditVisualRequest(string? VisualSpecMarkup, IReadOnlyList<VisualAssetInput>? Assets);
