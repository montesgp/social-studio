using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Batches;
using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.ApiService.Endpoints.Social.ContentGeneration;

/// <summary>
/// Multi-piece batch generation — the composition cart.
///
/// POST /api/social/single-analyses/{analysisId}/batches      — start a batch of N pieces (202)
/// GET  /api/social/content-generation-batches/{batchId}/progress — poll one batch
/// GET  /api/social/content-generation-batches/active         — every unfinished batch for the tenant
///
/// The POST returns immediately with the resolved composition; the pieces drain through the existing
/// content generation worker. Nothing here blocks the caller, which is the entire point: the operator
/// keeps using the rest of Hive while the batch runs.
/// </summary>
public static class MapContentGenerationBatchEndpoints
{
    private static readonly Error OrganizationRequired =
        Error.Validation("tenant.required", "An organization context is required for this operation.");

    public static IEndpointRouteBuilder MapContentGenerationBatches(this IEndpointRouteBuilder app)
    {
        var analysisGroup = app.MapGroup("/api/social/single-analyses")
            .WithTags("Content Generation")
            .RequireAuthorization();

        analysisGroup.MapPost("/{analysisId:guid}/batches", StartBatchAsync)
            .WithName("StartContentGenerationBatch")
            .WithSummary("Start a background batch of N content pieces from a composition cart. Returns 202 with the resolved mix.")
            .Produces<CreateContentGenerationBatchResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var batchGroup = app.MapGroup("/api/social/content-generation-batches")
            .WithTags("Content Generation")
            .RequireAuthorization();

        batchGroup.MapGet("/{batchId:guid}/progress", GetBatchProgressAsync)
            .WithName("GetContentGenerationBatchProgress")
            .WithSummary("Live progress of one batch: pieces completed, failed, running, and which format is being generated now.")
            .Produces<ContentGenerationBatchProgress>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        batchGroup.MapGet("/active", GetActiveBatchesAsync)
            .WithName("ListActiveContentGenerationBatches")
            .WithSummary("Every unfinished batch for the current organization. Backs the global progress indicator.")
            .Produces<IReadOnlyList<ContentGenerationBatchProgress>>(StatusCodes.Status200OK);

        return app;
    }

    public static async Task<IResult> StartBatchAsync(
        Guid analysisId,
        StartContentGenerationBatchRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new CreateContentGenerationBatchCommand(
                OrganizationId: tenantId.Value,
                ClientId: body.ClientId,
                AnalysisId: analysisId,
                Composition: body.Composition ?? [],
                AssortedTotal: body.AssortedTotal,
                CorrelationId: body.CorrelationId),
            cancellationToken);

        // 202, not 200: the pieces have been accepted and queued, not produced.
        return result.Match(response => (IResult)TypedResults.Accepted(response.StatusUrl, response));
    }

    public static async Task<IResult> GetBatchProgressAsync(
        Guid batchId,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new ContentGenerationBatchProgressQuery(batchId, tenantId.Value),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    public static async Task<IResult> GetActiveBatchesAsync(
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new ActiveContentGenerationBatchesQuery(tenantId.Value),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }
}

/// <summary>
/// Request body for starting a batch.
///
/// <paramref name="Composition"/> holds the explicit tiles the operator dragged in.
/// <paramref name="AssortedTotal"/> is the "surprise me x N" tile. Both may be present: they are
/// merged into one cart and capped as a whole.
/// </summary>
public sealed record StartContentGenerationBatchRequest(
    Guid? ClientId,
    IReadOnlyList<BatchCompositionItem>? Composition,
    int? AssortedTotal,
    Guid? CorrelationId);
