using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Download;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Status;
using MediatR;

namespace Incoders.Hive.ApiService.Endpoints.Social.ContentGeneration;

/// <summary>
/// Maps the three content-generation API endpoints:
/// POST /api/social/single-analyzer/{analysisId}/generate-package — start a job
/// GET  /api/social/content-generation-jobs/{jobId}/status         — poll status
/// GET  /api/social/content-generation-jobs/{jobId}/package        — download ZIP
/// </summary>
public static class ContentGenerationEndpointsExtensions
{
    public static IEndpointRouteBuilder MapContentGenerationEndpoints(this IEndpointRouteBuilder app)
    {
        // Start generation from a specific analysis (extends the single-analyzer group path)
        var analyzerGroup = app.MapGroup("/api/social/single-analyzer")
            .WithTags("Content Generation")
            .RequireAuthorization();

        analyzerGroup.MapPost("/{analysisId:guid}/generate-package", ContentGenerationEndpointFacade.GeneratePackageAsync)
            .WithName("GenerateContentPackage")
            .WithSummary("Start a content package generation job for a completed analysis")
            .Produces<GenerateContentPackageResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Job status and download (shared group)
        var jobsGroup = app.MapGroup("/api/social/content-generation-jobs")
            .WithTags("Content Generation")
            .RequireAuthorization();

        jobsGroup.MapGet("/{jobId:guid}/status", ContentGenerationEndpointFacade.GetJobStatusAsync)
            .WithName("GetContentGenerationJobStatus")
            .WithSummary("Get the current status of a content generation job")
            .Produces<ContentGenerationJobStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        jobsGroup.MapGet("/{jobId:guid}/package", ContentGenerationEndpointFacade.DownloadPackageAsync)
            .WithName("DownloadGeneratedPackage")
            .WithSummary("Download the generated content package ZIP when the job is completed")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}

/// <summary>Request body for the generate-package endpoint.</summary>
public sealed record GeneratePackageRequest(
    Guid? ClientId,
    IReadOnlyList<string>? RequestedFormats);

/// <summary>
/// Minimal API handler delegates for content generation endpoints.
/// Isolated in static methods so tests can invoke the same logic without
/// running a full web host — matching the pattern from <c>SingleAnalyzerEndpointFacade</c>.
/// </summary>
public static class ContentGenerationEndpointFacade
{
    private static readonly Error OrganizationRequired =
        Error.Validation("tenant.required", "An organization context is required for this operation.");

    public static async Task<IResult> GeneratePackageAsync(
        Guid analysisId,
        GeneratePackageRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new GenerateContentPackageCommand(
                OrganizationId: tenantId.Value,
                ClientId: body.ClientId,
                AnalysisId: analysisId,
                RequestedFormats: body.RequestedFormats ?? [],
                CorrelationId: null),
            cancellationToken);

        return result.Match(response =>
            (IResult)TypedResults.Accepted(response.StatusUrl, response));
    }

    public static async Task<IResult> GetJobStatusAsync(
        Guid jobId,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new ContentGenerationJobStatusQuery(jobId, tenantId.Value),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    public static async Task<IResult> DownloadPackageAsync(
        Guid jobId,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new DownloadGeneratedPackageQuery(jobId, tenantId.Value),
            cancellationToken);

        return result.Match(response =>
            (IResult)TypedResults.File(
                response.ContentStream,
                contentType: "application/zip",
                fileDownloadName: $"content-package-{jobId}.zip"));
    }
}
