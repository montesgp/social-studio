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

        group.MapPost("/analyze", SingleAnalyzerEndpointFacade.AnalyzeAsync)
            .WithName("AnalyzeSingleContent")
            .WithSummary("Run the full scrape + AI analysis pipeline for a single social content URL")
            .Produces<SingleContentAnalysisResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
        // TODO (Risk 3): map provider timeout/rate-limit to 502/503/504 when ResultExtensions
        // gains typed mappings for IntegrationErrors.*Timeout and *RateLimited. For now those
        // domains errors fall through to 500 (current behavior pinned by endpoint tests).

        return app;
    }
}

/// <summary>
/// Internal facade that exposes the endpoint handler as a static method.
/// Split from the registration extension so unit tests can call the handler directly
/// without standing up a full WebApplication host.
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
                // CAP3-MUST-04: AI provider is never caller-selectable; it is always
                // resolved from the organization's integration configuration.
                PreferredExtractionProviderKey: null),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }
}

/// <summary>
/// Request DTO for the single-content analyze endpoint.
/// Intentionally omits PreferredAiProviderKey and PreferredAiModel per CAP3-MUST-04.
/// AI provider selection is always driven by the organization's integration configuration.
/// </summary>
public sealed record AnalyzeSingleContentRequest(
    string Url,
    Guid? ClientId);
