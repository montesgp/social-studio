using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Application.Features.Social.Workflow;
using Incoders.Hive.Domain.Social.Workflow;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.Social;

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

        group.MapGet("/{workflowId:guid}/results/{resultId:guid}/preview", GetPreviewAsync)
            .WithName("GetSocialContentWorkflowResultPreview")
            .WithSummary("Get analysis/script preview for one workflow result")
            .Produces<ContentWorkflowPreviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{workflowId:guid}/select", SetSelectionAsync)
            .WithName("SetSocialContentWorkflowSelection")
            .WithSummary("Persist selected result ids for workflow")
            .Produces<ContentWorkflowSelectionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{workflowId:guid}/results/{resultId:guid}/actions", RunSingleActionAsync)
            .WithName("RunSocialContentWorkflowSingleAction")
            .WithSummary("Run one action for a single workflow result")
            .Produces<ContentWorkflowActionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{workflowId:guid}/actions/bulk", RunBulkActionAsync)
            .WithName("RunSocialContentWorkflowBulkAction")
            .WithSummary("Run one action over many selected workflow results")
            .Produces<ContentWorkflowActionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{workflowId:guid}/generate-package", GeneratePackageAsync)
            .WithName("GenerateSocialContentWorkflowPackage")
            .WithSummary("Generate simulated monthly package from selected results")
            .Produces<ContentWorkflowPackageResponse>(StatusCodes.Status200OK)
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
                body.NetworkSources),
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

    private static async Task<IResult> GetPreviewAsync(
        Guid workflowId,
        Guid resultId,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new GetContentWorkflowPreviewQuery(organizationId.Value, workflowId, resultId),
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

    private static async Task<IResult> RunSingleActionAsync(
        Guid workflowId,
        Guid resultId,
        [FromBody] RunSocialContentWorkflowActionRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new RunContentWorkflowActionCommand(
                organizationId.Value,
                workflowId,
                body.Action,
                [resultId]),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    private static async Task<IResult> RunBulkActionAsync(
        Guid workflowId,
        [FromBody] RunBulkSocialContentWorkflowActionRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new RunContentWorkflowActionCommand(
                organizationId.Value,
                workflowId,
                body.Action,
                body.ResultIds ?? Array.Empty<Guid>()),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    private static async Task<IResult> GeneratePackageAsync(
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
            new GenerateContentWorkflowPackageCommand(organizationId.Value, workflowId),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
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

    private static async Task<IResult> ListHistoryAsync(
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 12)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentWorkflowErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new ListContentWorkflowHistoryQuery(organizationId.Value, limit),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }
}

public sealed record StartSocialContentWorkflowRequest(
    Guid ClientId,
    string ClientName,
    string SearchCriterion,
    string StrategyMode,
    IReadOnlyList<string> NetworkSources);

public sealed record SelectSocialContentWorkflowRequest(IReadOnlyList<Guid>? ResultIds);

public sealed record RunSocialContentWorkflowActionRequest(string Action);

public sealed record RunBulkSocialContentWorkflowActionRequest(
    string Action,
    IReadOnlyList<Guid>? ResultIds);
