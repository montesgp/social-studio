using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Catalog;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Download;
using Incoders.Hive.Application.Features.Social.ContentGeneration.GeneratePackage;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Status;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Incoders.Hive.Tests.Api.Social.ContentGeneration;

/// <summary>
/// Unit tests for the content generation endpoint handler logic.
/// Mirrors the pattern from <c>SingleAnalyzerEndpointTests</c>: the endpoint
/// handler logic is expressed as pure static functions inside <c>EndpointHandlers</c>
/// so tests can validate the mapping contract without standing up a full web host.
/// </summary>
public sealed class ContentGenerationEndpointTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid AnalysisId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();

    // ─── POST /generate-package — happy path ─────────────────────────────────

    [Fact]
    public async Task GeneratePackage_ValidRequest_Returns202WithJobId()
    {
        var statusUrl = $"/api/social/content-generation-jobs/{JobId}/status";
        var response = new GenerateContentPackageResponse(JobId, statusUrl);
        var sender = new FakeSender<GenerateContentPackageCommand, GenerateContentPackageResponse>(
            Result.Success(response));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandlers.GeneratePackageAsync(
            analysisId: AnalysisId,
            requestedFormats: ["story-carousel", "story"],
            sender: sender,
            tenant: tenant);

        var accepted = Assert.IsType<Accepted<GenerateContentPackageResponse>>(result);
        Assert.NotNull(accepted.Value);
        Assert.Equal(JobId, accepted.Value!.JobId);
    }

    // ─── POST /generate-package — missing tenant header ─────────────────────

    [Fact]
    public async Task GeneratePackage_MissingTenant_ReturnsBadRequest()
    {
        var sender = new FakeSender<GenerateContentPackageCommand, GenerateContentPackageResponse>(
            Result.Success(new GenerateContentPackageResponse(JobId, "/")));
        var tenant = new FakeTenant(null); // no tenant

        var result = await EndpointHandlers.GeneratePackageAsync(
            analysisId: AnalysisId,
            requestedFormats: ["story-carousel"],
            sender: sender,
            tenant: tenant);

        Assert.IsNotType<Accepted<GenerateContentPackageResponse>>(result);
        Assert.Null(sender.LastCommand); // sender not called
    }

    // ─── POST /generate-package — validation error returns 400 ───────────────

    [Fact]
    public async Task GeneratePackage_ValidationError_Returns400()
    {
        var sender = new FakeSender<GenerateContentPackageCommand, GenerateContentPackageResponse>(
            Result.Failure<GenerateContentPackageResponse>(
                Error.Validation("content-gen.empty-formats", "requestedFormats must not be empty")));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandlers.GeneratePackageAsync(
            analysisId: AnalysisId,
            requestedFormats: [],
            sender: sender,
            tenant: tenant);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    // ─── POST /generate-package — cross-tenant analysis returns 404 ──────────

    [Fact]
    public async Task GeneratePackage_CrossTenantAnalysis_Returns404()
    {
        var sender = new FakeSender<GenerateContentPackageCommand, GenerateContentPackageResponse>(
            Result.Failure<GenerateContentPackageResponse>(
                Error.NotFound("content-gen.analysis-not-found", "Analysis not found")));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandlers.GeneratePackageAsync(
            analysisId: Guid.NewGuid(),
            requestedFormats: ["story-carousel"],
            sender: sender,
            tenant: tenant);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    // ─── GET /formats — contract test (S4-T1 / SCENARIO-12 / SCENARIO-13) ──────

    [Fact]
    public async Task ListFormatCatalog_ReturnsDeliverableEntries_AndDoesNotIncludeBareCarousel()
    {
        var entries = new ListFormatCatalogQueryHandler().ExecuteQuery();
        var sender = new FakeSender<ListFormatCatalogQuery, ListFormatCatalogResponse>(
            Result.Success(new ListFormatCatalogResponse(Entries: entries)));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandlers.ListFormatCatalogAsync(sender, tenant);

        var ok = Assert.IsType<Ok<ListFormatCatalogResponse>>(result);
        Assert.NotNull(ok.Value);
        var responseEntries = ok.Value!.Entries;

        // SCENARIO-12: all 5 deliverable keys present
        Assert.Contains(responseEntries, e => e.FormatKey == "story-carousel");
        Assert.Contains(responseEntries, e => e.FormatKey == "feed-carousel");
        Assert.Contains(responseEntries, e => e.FormatKey == "story");
        Assert.Contains(responseEntries, e => e.FormatKey == "feed-post");
        Assert.Contains(responseEntries, e => e.FormatKey == "reel-script");

        // SCENARIO-13: bare "carousel" is NOT in the response
        Assert.DoesNotContain(responseEntries, e => e.FormatKey == "carousel");
    }

    // ─── GET /status — happy path ─────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_ExistingJob_Returns200WithStatus()
    {
        var statusResponse = new ContentGenerationJobStatusResponse(
            JobId: JobId,
            Status: "Running",
            PerFormatStatus: new Dictionary<string, string> { ["carousel"] = "Generated" },
            ManifestJson: null);

        var sender = new FakeSender<ContentGenerationJobStatusQuery, ContentGenerationJobStatusResponse>(
            Result.Success(statusResponse));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandlers.GetJobStatusAsync(
            jobId: JobId,
            sender: sender,
            tenant: tenant);

        var ok = Assert.IsType<Ok<ContentGenerationJobStatusResponse>>(result);
        Assert.Equal("Running", ok.Value!.Status);
    }

    // ─── GET /status — cross-tenant returns 404 ──────────────────────────────

    [Fact]
    public async Task GetStatus_CrossTenantJob_Returns404()
    {
        var sender = new FakeSender<ContentGenerationJobStatusQuery, ContentGenerationJobStatusResponse>(
            Result.Failure<ContentGenerationJobStatusResponse>(
                Error.NotFound("content-gen.job-not-found", "Job not found")));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandlers.GetJobStatusAsync(
            jobId: Guid.NewGuid(),
            sender: sender,
            tenant: tenant);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    // ─── GET /status — missing tenant returns 400 ────────────────────────────

    [Fact]
    public async Task GetStatus_MissingTenant_ReturnsBadRequest()
    {
        var sender = new FakeSender<ContentGenerationJobStatusQuery, ContentGenerationJobStatusResponse>(
            Result.Success(new ContentGenerationJobStatusResponse(JobId, "Queued", new Dictionary<string, string>(), null)));
        var tenant = new FakeTenant(null);

        var result = await EndpointHandlers.GetJobStatusAsync(
            jobId: JobId,
            sender: sender,
            tenant: tenant);

        Assert.IsNotType<Ok<ContentGenerationJobStatusResponse>>(result);
        Assert.Null(sender.LastCommand);
    }

    // ─── GET /package — happy path streams ZIP ───────────────────────────────

    [Fact]
    public async Task DownloadPackage_CompletedJob_Returns200WithStream()
    {
        var stream = new MemoryStream([0x50, 0x4B, 0x03, 0x04]);
        var downloadResponse = new DownloadGeneratedPackageResponse(
            StorageRef: new StoredPackageRef("orgs/job/package.zip"),
            ContentStream: stream);

        var sender = new FakeSender<DownloadGeneratedPackageQuery, DownloadGeneratedPackageResponse>(
            Result.Success(downloadResponse));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandlers.DownloadPackageAsync(
            jobId: JobId,
            sender: sender,
            tenant: tenant);

        // Should return a file result (application/zip)
        Assert.IsType<FileStreamHttpResult>(result);
    }

    // ─── GET /package — not completed returns 404 ────────────────────────────

    [Fact]
    public async Task DownloadPackage_JobNotCompleted_Returns404()
    {
        var sender = new FakeSender<DownloadGeneratedPackageQuery, DownloadGeneratedPackageResponse>(
            Result.Failure<DownloadGeneratedPackageResponse>(
                Error.NotFound("content-gen.package-not-ready", "Package not available")));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandlers.DownloadPackageAsync(
            jobId: JobId,
            sender: sender,
            tenant: tenant);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    // ─── GET /package — cross-tenant returns 404 ─────────────────────────────

    [Fact]
    public async Task DownloadPackage_CrossTenantJob_Returns404()
    {
        var sender = new FakeSender<DownloadGeneratedPackageQuery, DownloadGeneratedPackageResponse>(
            Result.Failure<DownloadGeneratedPackageResponse>(
                Error.NotFound("content-gen.job-not-found", "Job not found")));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandlers.DownloadPackageAsync(
            jobId: Guid.NewGuid(),
            sender: sender,
            tenant: tenant);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }
}

// ─── Local endpoint handler mirror (avoids ApiService project reference) ─────

/// <summary>
/// Pure static handler functions that mirror the ApiService endpoint logic.
/// These are kept in sync with <c>ContentGenerationEndpointFacade</c> in ApiService.
/// The pattern is identical to what <c>SingleAnalyzerEndpointTests</c> uses.
/// </summary>
file static class EndpointHandlers
{
    private static readonly Error OrganizationRequired =
        Error.Validation("tenant.required", "An organization context is required for this operation.");

    public static async Task<IResult> GeneratePackageAsync(
        Guid analysisId,
        IReadOnlyList<string> requestedFormats,
        ISender sender,
        ICurrentTenant tenant)
    {
        if (tenant.TenantId is not { } tenantId)
            return ProblemFor(OrganizationRequired);

        var result = await sender.Send(
            new GenerateContentPackageCommand(
                OrganizationId: tenantId.Value,
                ClientId: null,
                AnalysisId: analysisId,
                RequestedFormats: requestedFormats,
                CorrelationId: null),
            CancellationToken.None);

        return result.IsSuccess
            ? TypedResults.Accepted(result.Value.StatusUrl, result.Value)
            : ProblemFor(result.Error);
    }

    public static async Task<IResult> GetJobStatusAsync(
        Guid jobId,
        ISender sender,
        ICurrentTenant tenant)
    {
        if (tenant.TenantId is not { } tenantId)
            return ProblemFor(OrganizationRequired);

        var result = await sender.Send(
            new ContentGenerationJobStatusQuery(jobId, tenantId.Value),
            CancellationToken.None);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : ProblemFor(result.Error);
    }

    public static async Task<IResult> DownloadPackageAsync(
        Guid jobId,
        ISender sender,
        ICurrentTenant tenant)
    {
        if (tenant.TenantId is not { } tenantId)
            return ProblemFor(OrganizationRequired);

        var result = await sender.Send(
            new DownloadGeneratedPackageQuery(jobId, tenantId.Value),
            CancellationToken.None);

        return result.IsSuccess
            ? TypedResults.File(
                result.Value.ContentStream,
                contentType: "application/zip",
                fileDownloadName: $"content-package-{jobId}.zip")
            : ProblemFor(result.Error);
    }

    public static async Task<IResult> ListFormatCatalogAsync(
        ISender sender,
        ICurrentTenant tenant)
    {
        if (tenant.TenantId is not { } _)
            return ProblemFor(OrganizationRequired);

        var result = await sender.Send(new ListFormatCatalogQuery(), CancellationToken.None);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : ProblemFor(result.Error);
    }

    private static IResult ProblemFor(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };
        return TypedResults.Problem(
            title: error.Type == ErrorType.NotFound ? "Resource not found" : "Validation failed",
            detail: error.MessageKey,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}

// ─── Test doubles ─────────────────────────────────────────────────────────────

file sealed class FakeTenant : ICurrentTenant
{
    public FakeTenant(Guid? tenantId)
        => TenantId = tenantId.HasValue ? new TenantId(tenantId.Value) : null;

    public TenantId? TenantId { get; }
    public bool IsResolved => TenantId.HasValue;
}

file sealed class FakeSender<TRequest, TResponse> : ISender
    where TRequest : IRequest<Result<TResponse>>
{
    private readonly Result<TResponse> _result;
    public object? LastCommand { get; private set; }

    public FakeSender(Result<TResponse> result) => _result = result;

    public Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken cancellationToken = default)
    {
        LastCommand = request;
        if (request is TRequest)
            return Task.FromResult((TOut)(object)_result);
        throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
    }

    public Task Send<TReq>(TReq request, CancellationToken cancellationToken = default)
        where TReq : IRequest => Task.CompletedTask;

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        Task.FromResult<object?>(null);

    public IAsyncEnumerable<TOut> CreateStream<TOut>(IStreamRequest<TOut> request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
