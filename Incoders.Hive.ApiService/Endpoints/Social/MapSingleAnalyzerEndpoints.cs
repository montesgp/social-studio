using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using MediatR;

namespace Incoders.Hive.ApiService.Endpoints.Social;

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

        // Poll: returns the current status (+ result when Analyzed, failure when Failed).
        group.MapGet("/analyze/{id:guid}", SingleAnalyzerEndpointFacade.GetAsync)
            .WithName("GetSingleContentAnalysis")
            .WithSummary("Get the current status/result of a single-content analysis")
            .Produces<SingleAnalyzerStatusResponse>(StatusCodes.Status200OK)
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
}

/// <summary>
/// Request DTO for the single-content analyze endpoint.
/// Intentionally omits AI provider/model per CAP3-MUST-04 — resolved from org integrations.
/// </summary>
public sealed record AnalyzeSingleContentRequest(
    string Url,
    Guid? ClientId);
