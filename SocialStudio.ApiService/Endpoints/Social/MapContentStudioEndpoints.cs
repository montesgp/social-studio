using SocialStudio.ApiService.Endpoints.Common;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Features.Social.ContentGeneration.Visual.Fonts;
using SocialStudio.Application.Features.Social.ContentStudio;
using SocialStudio.Application.Features.Social.ContentStudio.Bulk;
using SocialStudio.Application.Features.Social.ContentStudio.Export;
using SocialStudio.Application.Features.Social.ContentStudio.Materialize;
using SocialStudio.Application.Features.Social.ContentStudio.Operations;
using SocialStudio.Application.Features.Social.ContentStudio.Queries;
using SocialStudio.Application.Features.Social.ContentStudio.Stage;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace SocialStudio.ApiService.Endpoints.Social;

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

        // client-style-fidelity CSF-CS-01: the bundled font catalog, anonymous (OFL files).
        app.MapContentStudioFontEndpoints();

        group.MapPost("/runs/{jobId:guid}/materialize", MaterializeAsync)
            .WithName("MaterializeContentStudioRun")
            .WithSummary("Lazily (idempotently) materialize the Content Studio set for a completed generation run")
            .Produces<ContentSetResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/analyses/{analysisId:guid}/materialize", MaterializeAnalysisAsync)
            .WithName("MaterializeContentStudioAnalysis")
            .WithSummary("Lazily (idempotently) materialize the Content Studio set for ALL formats a client generated from an analysis (aggregates every generation job)")
            .Produces<ContentSetResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/sets/{setId:guid}", GetSetAsync)
            .WithName("GetContentStudioSet")
            .Produces<ContentSetResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/sets/{setId:guid}/items", ListItemsAsync)
            .WithName("ListContentStudioItems")
            .Produces<IReadOnlyList<ContentItemResponse>>(StatusCodes.Status200OK);

        group.MapPost("/items/by-ids", ListItemsByIdsAsync)
            .WithName("ListContentStudioItemsByIds")
            .WithSummary("Load an explicit, possibly cross-set selection of items for review")
            .Produces<IReadOnlyList<ContentItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/sets/{setId:guid}/export.pptx", ExportPptxAsync)
            .WithName("ExportContentStudioPresentationPptx")
            .WithSummary("Export the set's reviewed visual content as a client-presentation PPTX deck")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/items/{itemId:guid}", GetItemAsync)
            .WithName("GetContentStudioItem")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/items/{itemId:guid}/preview", PreviewAsync)
            .WithName("GetContentStudioItemPreview")
            .Produces<ContentPreviewProjection>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/items/{itemId:guid}/assets/{assetIndex:int}", AssetAsync)
            .WithName("GetContentStudioItemAsset")
            .WithSummary("Streams a rendered asset referenced by a Content Studio item")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/render-capabilities", GetRenderCapabilitiesAsync)
            .WithName("GetContentStudioRenderCapabilities")
            .WithSummary("The picker/render font contract for a client (brand fonts + system-safe list) plus the color-emoji render-capability signal")
            .Produces<RenderCapabilitiesResponse>(StatusCodes.Status200OK);

        group.MapPut("/items/{itemId:guid}/text", EditTextAsync)
            .WithName("EditContentStudioItemText")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/items/{itemId:guid}/caption", EditCaptionAsync)
            .WithName("EditContentStudioItemCaption")
            .WithSummary("Edit the dedicated publication caption (distinct from the reel-script text field)")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
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

        group.MapPut("/items/{itemId:guid}/plan-link", LinkPlanAsync)
            .WithName("LinkContentStudioItemToPlan")
            .WithSummary("Associate the item with a content-plan calendar slot")
            .Produces<ContentItemResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/items/{itemId:guid}/plan-link", UnlinkPlanAsync)
            .WithName("UnlinkContentStudioItemFromPlan")
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

        group.MapPost("/items/{itemId:guid}/reopen", ReopenAsync)
            .WithName("ReopenContentStudioItem")
            .WithSummary("Bring a discarded/rejected item back into review so it becomes editable again")
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

        group.MapPost("/items/bulk", BulkAsync)
            .WithName("BulkContentStudioOperation")
            .WithSummary("Apply approve/reject/discard/delete across a selection (partial-success)")
            .Produces<BulkOperationResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

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

    private static async Task<IResult> MaterializeAnalysisAsync(
        Guid analysisId, [FromBody] MaterializeRunRequest body, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new MaterializeAnalysisCommand(org.Value, body.ClientId, analysisId), ct);
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

    // POST for a read: the selection is a request BODY because a Bank triage can carry dozens of ids,
    // which a query string would push against URL length limits and truncate silently.
    private static async Task<IResult> ListItemsByIdsAsync(
        [FromBody] ListItemsByIdsRequest body, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new ListContentItemsByIdsQuery(org.Value, body.ItemIds ?? []), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> ExportPptxAsync(
        Guid setId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new ExportPresentationQuery(org.Value, setId), ct);
        return result.Match(r => (IResult)TypedResults.File(
            r.PptxBytes,
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            r.DownloadFilename));
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

    private static async Task<IResult> AssetAsync(
        Guid itemId, int assetIndex, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new GetContentItemAssetQuery(org.Value, itemId, assetIndex), ct);
        return result.Match(r => (IResult)TypedResults.Stream(
            r.Content,
            r.Mime,
            fileDownloadName: null,
            lastModified: null,
            entityTag: null,
            enableRangeProcessing: false));
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

    private static async Task<IResult> GetRenderCapabilitiesAsync(
        [FromQuery] Guid clientId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new GetRenderCapabilitiesQuery(org.Value, clientId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> EditCaptionAsync(
        Guid itemId, [FromBody] EditCaptionRequest body, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new EditItemCaptionCommand(org.Value, itemId, body.Caption), ct);
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

    private static async Task<IResult> LinkPlanAsync(
        Guid itemId, [FromBody] PlanLinkRequest body, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(
            new LinkItemToPlanCommand(
                org.Value, itemId, body.ContentPlanId, body.PeriodKind, body.Year, body.Month, body.CalendarSlotId),
            ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UnlinkPlanAsync(
        Guid itemId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new UnlinkItemFromPlanCommand(org.Value, itemId), ct);
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

    private static async Task<IResult> ReopenAsync(
        Guid itemId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new ReopenItemCommand(org.Value, itemId), ct);
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
        if (tenant.TenantId is not { } org || currentUser.UserId is null)
        {
            return TypedResults.Unauthorized();
        }

        // The approving user is already recorded on the item by ApproveItemCommand; staging only
        // moves Approved → ReadyForPublication and no longer emits a handoff payload that needed it.
        var result = await sender.Send(new StageItemForPublicationCommand(org.Value, itemId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> BulkAsync(
        [FromBody] BulkOperationRequest body, ICurrentTenant tenant, ICurrentUser currentUser, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org || currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        if (!Enum.TryParse<BulkItemOperation>(body.Operation, ignoreCase: true, out var operation))
        {
            return TypedResults.Problem(
                title: "Validation failed",
                detail: $"Unknown bulk operation '{body.Operation}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await sender.Send(
            new BulkItemOperationCommand(org.Value, userId, body.ItemIds ?? [], operation, body.Reason),
            ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }
}

public sealed record MaterializeRunRequest(Guid ClientId);

public sealed record EditTextRequest(string Text);

public sealed record EditCaptionRequest(string Caption);

public sealed record RejectRequest(string Reason);

public sealed record EditVisualRequest(string? VisualSpecMarkup, IReadOnlyList<VisualAssetInput>? Assets);

public sealed record BulkOperationRequest(IReadOnlyList<Guid> ItemIds, string Operation, string? Reason);

public sealed record ListItemsByIdsRequest(IReadOnlyList<Guid> ItemIds);

public sealed record PlanLinkRequest(Guid ContentPlanId, short PeriodKind, int Year, int Month, Guid CalendarSlotId);
