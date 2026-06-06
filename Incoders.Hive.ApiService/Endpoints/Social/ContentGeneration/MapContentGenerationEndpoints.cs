using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Assemble;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Download;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Export;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.PackageListing;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Regenerate;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Status;
using MediatR;

namespace Incoders.Hive.ApiService.Endpoints.Social.ContentGeneration;

/// <summary>
/// Maps the content-generation API endpoints:
/// POST /api/social/single-analyzer/{analysisId}/generate-package         — start a job
/// POST /api/social/single-analyzer/{analysisId}/package/assemble         — assemble ZIP on demand
/// GET  /api/social/content-generation-jobs/{jobId}/status                — poll status
/// GET  /api/social/content-generation-jobs/{jobId}/package               — legacy frozen-ZIP download
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

        analyzerGroup.MapPost("/{analysisId:guid}/package/assemble", ContentGenerationEndpointFacade.AssemblePackageAsync)
            .WithName("AssembleGeneratedPackage")
            .WithSummary("Assemble a ZIP on demand from currently-stored per-asset rows for the analysis")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status404NotFound);

        analyzerGroup.MapPost("/{analysisId:guid}/formats/{formatKey}/regenerate", ContentGenerationEndpointFacade.RegenerateFormatAsync)
            .WithName("RegenerateFormatAsset")
            .WithSummary("Re-render a single format and overwrite the stored asset(s) for the given client")
            .Produces<RegenerateFormatResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        analyzerGroup.MapGet("/{analysisId:guid}/formats/{formatKey}/download-pptx", ContentGenerationEndpointFacade.DownloadFormatAsPptxAsync)
            .WithName("DownloadFormatAsPptx")
            .WithSummary("Download a generated format as an editable PPTX presentation (offline, no re-render). Requires jobId query parameter.")
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.openxmlformats-officedocument.presentationml.presentation")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        analyzerGroup.MapDelete("/{analysisId:guid}/formats/{formatKey}", ContentGenerationEndpointFacade.DeleteFormatAsync)
            .WithName("DeleteFormatAsset")
            .WithSummary("Physically delete a format's asset object(s) from storage and remove the DB rows")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        analyzerGroup.MapGet("/{analysisId:guid}/packages", ContentGenerationEndpointFacade.ListPackagesAsync)
            .WithName("ListAnalysisPackages")
            .WithSummary("Returns the per-client package tree (formats + assets) for the given analysis, sourced from DB")
            .Produces<AnalysisPackagesApiResponse>(StatusCodes.Status200OK)
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
    IReadOnlyList<string>? RequestedFormats,
    string? RegenerationObjection);

/// <summary>
/// Per-client selection entry for the assemble endpoint.
/// A null or absent <see cref="FormatKeys"/> means "include all stored assets for this client".
/// </summary>
public sealed record AssembleClientSelectionRequest(
    Guid ClientId,
    IReadOnlyList<string>? FormatKeys);

/// <summary>Request body for the assemble-package endpoint.</summary>
public sealed record AssemblePackageRequest(
    IReadOnlyList<AssembleClientSelectionRequest>? Clients);

/// <summary>Request body for the regenerate-format endpoint.</summary>
public sealed record RegenerateFormatRequest(
    Guid ClientId,
    string? Objection);

/// <summary>One regenerated asset entry returned by the regenerate endpoint.</summary>
public sealed record RegenerateFormatAssetItem(string StoragePath, string Filename, string Mime, long SizeBytes);

/// <summary>Response body for the regenerate-format endpoint.</summary>
public sealed record RegenerateFormatResponse(string FormatKey, IReadOnlyList<RegenerateFormatAssetItem> Assets);

// ─── List Packages API response types ─────────────────────────────────────────

/// <summary>One stored asset entry in the packages response.</summary>
public sealed record AssetPackageApiEntry(int Index, string Filename, string StoragePath, long SizeBytes);

/// <summary>One format's entries for a client in the packages response.</summary>
public sealed record FormatPackageApiEntry(
    string FormatKey,
    string Filename,
    string Mime,
    IReadOnlyList<AssetPackageApiEntry> Assets);

/// <summary>One client's accumulated formats in the packages response.</summary>
public sealed record ClientPackageApiEntry(
    Guid ClientId,
    IReadOnlyList<FormatPackageApiEntry> Formats);

/// <summary>Top-level response for GET .../packages.</summary>
public sealed record AnalysisPackagesApiResponse(IReadOnlyList<ClientPackageApiEntry> Clients);

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
                CorrelationId: null,
                RegenerationObjection: body.RegenerationObjection),
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

    public static async Task<IResult> AssemblePackageAsync(
        Guid analysisId,
        AssemblePackageRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return OrganizationRequired.ToProblem();

        // Build the selection — default to "all clients" sentinel when no body clients provided.
        // An empty clients list means "include everything stored for this analysis".
        var clients = body.Clients is { Count: > 0 }
            ? body.Clients
                .Select(c => new ClientSelection(c.ClientId, c.FormatKeys))
                .ToList()
            : [new ClientSelection(Guid.Empty, null)];  // wildcard: handler will expand

        var selection = new AssembleSelection(clients);

        var result = await sender.Send(
            new AssembleGeneratedPackageQuery(analysisId, tenantId.Value, selection),
            cancellationToken);

        return result.Match(response =>
            (IResult)TypedResults.File(
                response.ZipStream,
                contentType: "application/zip",
                fileDownloadName: response.DownloadFilename));
    }

    public static async Task<IResult> RegenerateFormatAsync(
        Guid analysisId,
        string formatKey,
        RegenerateFormatRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new RegenerateFormatAssetCommand(
                AnalysisId: analysisId,
                OrganizationId: tenantId.Value,
                ClientId: body.ClientId,
                FormatKey: formatKey,
                Objection: body.Objection),
            cancellationToken);

        return result.Match(response =>
            (IResult)TypedResults.Ok(new RegenerateFormatResponse(
                response.FormatKey,
                response.Assets.Select(a => new RegenerateFormatAssetItem(
                    a.StoragePath, a.Filename, a.Mime, a.SizeBytes)).ToList())));
    }

    public static async Task<IResult> DeleteFormatAsync(
        Guid analysisId,
        string formatKey,
        Guid clientId,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new DeleteFormatAssetCommand(
                AnalysisId: analysisId,
                OrganizationId: tenantId.Value,
                ClientId: clientId,
                FormatKey: formatKey),
            cancellationToken);

        return result.Match(() => (IResult)TypedResults.NoContent());
    }

    /// <summary>
    /// GET /{analysisId}/formats/{formatKey}/download-pptx?jobId={jobId}
    ///
    /// Downloads the persisted format composition spec as an editable PPTX.
    /// Requires the <c>jobId</c> query parameter that produced this format result.
    /// Returns 409 when the format predates B3 markup persistence (PPTX-02-d).
    /// No re-render, no AI call (PPTX-03).
    /// </summary>
    public static async Task<IResult> DownloadFormatAsPptxAsync(
        Guid analysisId,
        string formatKey,
        Guid jobId,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new ExportFormatToPptxQuery(
                JobId: jobId,
                FormatKey: formatKey,
                OrganizationId: tenantId.Value),
            cancellationToken);

        return result.Match(response =>
            (IResult)TypedResults.File(
                response.PptxBytes,
                contentType: "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                fileDownloadName: response.DownloadFilename));
    }

    public static async Task<IResult> ListPackagesAsync(
        Guid analysisId,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new ListAnalysisPackagesQuery(analysisId, tenantId.Value),
            cancellationToken);

        return result.Match(response =>
            (IResult)TypedResults.Ok(new AnalysisPackagesApiResponse(
                response.Clients
                    .Select(c => new ClientPackageApiEntry(
                        c.ClientId,
                        c.Formats
                            .Select(f => new FormatPackageApiEntry(
                                f.FormatKey,
                                f.Filename,
                                f.Mime,
                                f.Assets
                                    .Select(a => new AssetPackageApiEntry(
                                        a.Index, a.Filename, a.StoragePath, a.SizeBytes))
                                    .ToList()))
                            .ToList()))
                    .ToList())));
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
