using SocialStudio.ApiService.Endpoints.Common;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Common.Pagination;
using SocialStudio.Application.Features.Social.ContentGeneration.Batches;
using SocialStudio.Application.Features.Social.Workflow;
using SocialStudio.Application.Features.Social.Workflow.Enqueue;
using SocialStudio.Domain.Social.Workflow;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace SocialStudio.ApiService.Endpoints.Social;

public static class SocialContentWorkflowEndpointsExtensions
{
    public static IEndpointRouteBuilder MapSocialContentWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/workflows")
            .WithTags("Social Content Workflow")
            .RequireAuthorization();

        group.MapPost("/content-generation", StartAsync)
            .WithName("StartSocialContentWorkflow")
            .WithSummary("Initialize and run social content generation workflow")
            .Produces<ContentWorkflowSessionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{workflowId:guid}/status", GetStatusAsync)
            .WithName("GetSocialContentWorkflowStatus")
            .WithSummary("Get workflow progress and step-level status")
            .Produces<ContentWorkflowStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{workflowId:guid}/results", GetResultsAsync)
            .WithName("GetSocialContentWorkflowResults")
            .WithSummary("Get paginated workflow results")
            .Produces<PageResult<ContentWorkflowResultResponse>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{workflowId:guid}/select", SetSelectionAsync)
            .WithName("SetSocialContentWorkflowSelection")
            .WithSummary("Persist selected result ids for workflow")
            .Produces<ContentWorkflowSelectionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{workflowId:guid}/enqueue", EnqueueAsync)
            .WithName("EnqueueSocialContentWorkflowCandidates")
            .WithSummary("Enqueue per-candidate analysis-then-batch sequences from the composed formats")
            .Produces<EnqueueContentWorkflowCandidatesResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{workflowId:guid}/history", GetHistoryAsync)
            .WithName("GetSocialContentWorkflowHistory")
            .WithSummary("Get one workflow history and action execution trail")
            .Produces<ContentWorkflowHistoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/history", ListHistoryAsync)
            .WithName("ListSocialContentWorkflowHistory")
            .WithSummary("List recent workflow sessions for resolved organization")
            .Produces<IReadOnlyList<ContentWorkflowHistoryItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{workflowId:guid}/candidates/{candidateId:guid}/recover", RecoverCandidateAsync)
            .WithName("RecoverSocialContentWorkflowCandidate")
            .WithSummary("Bring a processed candidate back into the active candidate pool")
            .Produces<ContentWorkflowCandidateStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> StartAsync(
        [FromBody] StartSocialContentWorkflowRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new StartContentWorkflowCommand(
                organizationId.Value,
                body.ClientId,
                body.ClientName,
                body.SearchCriterion,
                body.StrategyMode,
                body.NetworkSources,
                body.ForceRefresh,
                body.OriginMode),
            cancellationToken);

        return result.Match(workflow => (IResult)TypedResults.Created($"/api/social/workflows/{workflow.WorkflowId}/status", workflow));
    }

    private static async Task<IResult> GetStatusAsync(
        Guid workflowId,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new GetContentWorkflowStatusQuery(organizationId.Value, workflowId),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    private static async Task<IResult> GetResultsAsync(
        Guid workflowId,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new GetContentWorkflowResultsQuery(organizationId.Value, workflowId, page, pageSize),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    private static async Task<IResult> SetSelectionAsync(
        Guid workflowId,
        [FromBody] SelectSocialContentWorkflowRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new SelectContentWorkflowResultsCommand(organizationId.Value, workflowId, body.ResultIds ?? Array.Empty<Guid>()),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    internal static async Task<IResult> EnqueueAsync(
        Guid workflowId,
        [FromBody] EnqueueSocialContentWorkflowRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        if (currentUser.UserId is not { } userId)
        {
            return ContentWorkflowErrors.UserRequired.ToProblem();
        }

        var candidates = (body.Candidates ?? [])
            .Select(c => new WorkflowCandidateComposition(
                c.CandidateId,
                (c.Composition ?? []).Select(f => new BatchCompositionItem(f.FormatKey, f.Count)).ToList()))
            .ToList();

        var result = await sender.Send(
            new EnqueueContentWorkflowCandidatesCommand(organizationId.Value, body.ClientId, userId, workflowId, candidates),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Accepted((string?)null, response));
    }

    private static async Task<IResult> GetHistoryAsync(
        Guid workflowId,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new GetContentWorkflowHistoryQuery(organizationId.Value, workflowId),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    internal static async Task<IResult> ListHistoryAsync(
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 12,
        [FromQuery] Guid? clientId = null)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new ListContentWorkflowHistoryQuery(organizationId.Value, limit, clientId),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    private static async Task<IResult> RecoverCandidateAsync(
        Guid workflowId,
        Guid candidateId,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new RecoverContentWorkflowCandidateCommand(organizationId.Value, workflowId, candidateId),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }
}

/// <param name="ForceRefresh">
/// The operator's EXPLICIT opt-in to a billed provider run. Absent or <c>false</c> means "serve from the
/// stored candidate bank" — no client can start a paid scrape by forgetting a field.
/// </param>
/// <param name="OriginMode">
/// <c>discovery</c> (default when absent) reaches the market: scraping providers or the stored bank.
/// <c>ai-ideas</c> reaches nothing — the model invents topics from the client's own context, so no actor
/// runs and nothing is billed. An unrecognised value is REJECTED rather than defaulted, so a typo cannot
/// start the paid scrape the caller was avoiding. Spanish aliases (<c>ideas</c>, <c>ia</c>) are accepted.
/// </param>
public sealed record StartSocialContentWorkflowRequest(
    Guid ClientId,
    string ClientName,
    string SearchCriterion,
    string StrategyMode,
    IReadOnlyList<string> NetworkSources,
    bool ForceRefresh = false,
    string? OriginMode = null);

public sealed record SelectSocialContentWorkflowRequest(IReadOnlyList<Guid>? ResultIds);

public sealed record EnqueueFormatCompositionRequest(string FormatKey, int Count);

public sealed record EnqueueCandidateCompositionRequest(
    Guid CandidateId,
    IReadOnlyList<EnqueueFormatCompositionRequest>? Composition);

public sealed record EnqueueSocialContentWorkflowRequest(
    Guid ClientId,
    IReadOnlyList<EnqueueCandidateCompositionRequest>? Candidates);
