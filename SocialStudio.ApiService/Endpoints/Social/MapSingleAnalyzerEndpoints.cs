using SocialStudio.ApiService.Endpoints.Common;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Features.Social.SingleAnalyzer.Analyze;
using SocialStudio.Application.Features.Social.SingleAnalyzer.History;
using SocialStudio.Domain.Social.SingleAnalyzer;
using MediatR;

namespace SocialStudio.ApiService.Endpoints.Social;

public static class SingleAnalyzerEndpointsExtensions
{
    public static IEndpointRouteBuilder MapSingleAnalyzerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/single-analyzer")
            .WithTags("Social Single Analyzer")
            .RequireAuthorization();

        // Start: validates + pre-flights + queues the work; returns 202 with the analysis id/status.
        group.MapPost("/analyze", SingleAnalyzerEndpointFacade.AnalyzeAsync)
            .WithName("AnalyzeSingleContent")
            .WithSummary("Queue the scrape + AI analysis pipeline for a social content URL")
            .Produces<SingleAnalyzerStatusResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // History: paged list of analyses owned by the current user in the current org.
        group.MapGet("/analyze", SingleAnalyzerEndpointFacade.ListAsync)
            .WithName("ListSingleContentAnalyses")
            .WithSummary("List the current user's analyses for the active organization (paged, newest first)")
            .Produces<SingleContentAnalysisHistoryResponse>(StatusCodes.Status200OK);

        // Lookup: URL pre-flight so the UI can resume an existing run before spending providers again.
        group.MapGet("/analyze/lookup", SingleAnalyzerEndpointFacade.LookupByUrlAsync)
            .WithName("FindSingleContentAnalysisByUrl")
            .WithSummary("Find the current user's latest saved analysis for a submitted URL")
            .Produces<FindSingleContentAnalysisByUrlResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Poll: returns the current status (+ result when Analyzed, failure when Failed).
        group.MapGet("/analyze/{id:guid}", SingleAnalyzerEndpointFacade.GetAsync)
            .WithName("GetSingleContentAnalysis")
            .WithSummary("Get the current status/result of a single-content analysis")
            .Produces<SingleAnalyzerStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Retry-AI: re-runs only the AI stage using the persisted snapshot. NO extraction call.
        group.MapPost("/analyze/{id:guid}/retry-ai", SingleAnalyzerEndpointFacade.RetryAiAsync)
            .WithName("RetryAiOnAnalysis")
            .WithSummary("Re-run only the AI stage of an existing analysis (reusing the persisted snapshot)")
            .Produces<SingleAnalyzerStatusResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Hard delete: drops the analysis, its content-generation jobs and any package ZIP files.
        // Owner-scoped — a user can only delete their own analyses.
        group.MapDelete("/analyze/{id:guid}", SingleAnalyzerEndpointFacade.DeleteAsync)
            .WithName("DeleteSingleContentAnalysis")
            .WithSummary("Hard-delete an analysis owned by the current user")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}

/// <summary>
/// Internal facade exposing the endpoint handlers as static methods so unit tests can call them
/// directly without standing up a full WebApplication host.
/// </summary>
internal static class SingleAnalyzerEndpointFacade
{
    internal static async Task<IResult> AnalyzeAsync(
        AnalyzeSingleContentRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return SingleAnalyzerErrors.OrganizationRequired.ToProblem();
        }

        if (currentUser.UserId is not { } userId)
        {
            return SingleAnalyzerErrors.UserRequired.ToProblem();
        }

        var result = await sender.Send(
            new AnalyzeSingleContentCommand(
                OrganizationId: tenantId.Value,
                UserId: userId,
                Url: body.Url,
                ClientId: body.ClientId,
                // CAP3-MUST-04: AI provider is never caller-selectable; resolved from org integrations.
                PreferredExtractionProviderKey: null),
            cancellationToken);

        return result.Match(status =>
            (IResult)TypedResults.Accepted($"/api/social/single-analyzer/analyze/{status.Id}", status));
    }

    internal static async Task<IResult> GetAsync(
        Guid id,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return SingleAnalyzerErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new GetSingleContentAnalysisQuery(id, tenantId.Value),
            cancellationToken);

        return result.Match(status => (IResult)TypedResults.Ok(status));
    }

    internal static async Task<IResult> RetryAiAsync(
        Guid id,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return SingleAnalyzerErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new RetryAiAnalysisCommand(id, tenantId.Value),
            cancellationToken);

        return result.Match(status =>
            (IResult)TypedResults.Accepted($"/api/social/single-analyzer/analyze/{status.Id}", status));
    }

    internal static async Task<IResult> ListAsync(
        ISender sender,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        int? page = null,
        int? pageSize = null)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return SingleAnalyzerErrors.OrganizationRequired.ToProblem();
        }

        if (currentUser.UserId is not { } userId)
        {
            return SingleAnalyzerErrors.UserRequired.ToProblem();
        }

        var result = await sender.Send(
            new ListSingleContentAnalysesQuery(
                OrganizationId: tenantId.Value,
                UserId: userId,
                Page: page ?? 1,
                PageSize: pageSize ?? 20),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    internal static async Task<IResult> LookupByUrlAsync(
        string url,
        ISender sender,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return SingleAnalyzerErrors.OrganizationRequired.ToProblem();
        }

        if (currentUser.UserId is not { } userId)
        {
            return SingleAnalyzerErrors.UserRequired.ToProblem();
        }

        var result = await sender.Send(
            new FindSingleContentAnalysisByUrlQuery(
                OrganizationId: tenantId.Value,
                UserId: userId,
                Url: url),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    internal static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return SingleAnalyzerErrors.OrganizationRequired.ToProblem();
        }

        if (currentUser.UserId is not { } userId)
        {
            return SingleAnalyzerErrors.UserRequired.ToProblem();
        }

        var result = await sender.Send(
            new DeleteSingleContentAnalysisCommand(id, tenantId.Value, userId),
            cancellationToken);

        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

/// <summary>
/// Request DTO for the single-content analyze endpoint.
/// Intentionally omits AI provider/model per CAP3-MUST-04 — resolved from org integrations.
/// </summary>
public sealed record AnalyzeSingleContentRequest(
    string Url,
    Guid? ClientId);
