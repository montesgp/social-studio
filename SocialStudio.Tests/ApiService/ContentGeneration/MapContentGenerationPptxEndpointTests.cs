using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Features.Social.ContentGeneration.Export;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SocialStudio.Tests.ApiService.ContentGeneration;

/// <summary>
/// B3 WU-3 endpoint tests for the PPTX download route.
///
/// <para>
/// Tests validate the mapping between handler results and HTTP responses
/// without standing up a full web host — following the pattern from
/// <c>ContentGenerationEndpointTests</c>.
/// </para>
///
/// <para>Spec: PPTX-02 (download endpoint, correct MIME type, Content-Disposition).</para>
/// </summary>
public sealed class MapContentGenerationPptxEndpointTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();

    private const string PptxMimeType =
        "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    // ─── Happy path: bytes returned with correct MIME type ───────────────────

    [Fact]
    public async Task DownloadPptx_HandlerReturnsBytes_Returns200WithPptxMimeType()
    {
        var pptxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        var response = new ExportFormatToPptxResponse(pptxBytes, "carousel.pptx");

        var sender = new FakeSender<ExportFormatToPptxQuery, ExportFormatToPptxResponse>(
            Result.Success(response));
        var tenant = new FakeTenant(OrgId);

        var result = await DownloadPptxHandler.HandleAsync(
            jobId: JobId,
            formatKey: "carousel",
            sender: sender,
            tenant: tenant,
            cancellationToken: CancellationToken.None);

        var fileResult = Assert.IsType<FileContentHttpResult>(result);
        Assert.Equal(PptxMimeType, fileResult.ContentType);
        Assert.Equal("carousel.pptx", fileResult.FileDownloadName);
        Assert.Equal(pptxBytes, fileResult.FileContents.ToArray());
    }

    // ─── Not-exportable: markup absent → 409 Conflict ────────────────────────

    [Fact]
    public async Task DownloadPptx_NotExportableError_Returns409()
    {
        var error = Error.Conflict(
            "content-gen.not-exportable",
            "This format does not have a persisted composition spec.");

        var sender = new FakeSender<ExportFormatToPptxQuery, ExportFormatToPptxResponse>(
            Result.Failure<ExportFormatToPptxResponse>(error));
        var tenant = new FakeTenant(OrgId);

        var result = await DownloadPptxHandler.HandleAsync(
            jobId: JobId,
            formatKey: "carousel",
            sender: sender,
            tenant: tenant,
            cancellationToken: CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    // ─── Job not found → 404 ─────────────────────────────────────────────────

    [Fact]
    public async Task DownloadPptx_JobNotFound_Returns404()
    {
        var error = Error.NotFound("content-gen.job-not-found", "The specified job was not found.");

        var sender = new FakeSender<ExportFormatToPptxQuery, ExportFormatToPptxResponse>(
            Result.Failure<ExportFormatToPptxResponse>(error));
        var tenant = new FakeTenant(OrgId);

        var result = await DownloadPptxHandler.HandleAsync(
            jobId: JobId,
            formatKey: "carousel",
            sender: sender,
            tenant: tenant,
            cancellationToken: CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    // ─── Missing tenant → problem response ───────────────────────────────────

    [Fact]
    public async Task DownloadPptx_MissingTenant_ReturnsProblem()
    {
        var sender = new FakeSender<ExportFormatToPptxQuery, ExportFormatToPptxResponse>(
            Result.Success(new ExportFormatToPptxResponse([], "carousel.pptx")));
        var tenant = new FakeTenant(null); // no tenant

        var result = await DownloadPptxHandler.HandleAsync(
            jobId: JobId,
            formatKey: "carousel",
            sender: sender,
            tenant: tenant,
            cancellationToken: CancellationToken.None);

        Assert.IsType<ProblemHttpResult>(result);
    }
}

// ─── Pure handler mirror (avoids ApiService project reference in test project) ──

/// <summary>
/// Static mirror of the download-pptx endpoint handler logic.
/// Kept in sync with <c>ContentGenerationEndpointFacade.DownloadFormatAsPptxAsync</c>.
/// </summary>
file static class DownloadPptxHandler
{
    private static readonly Error OrganizationRequired =
        Error.Validation("tenant.required", "An organization context is required for this operation.");

    private const string PptxMimeType =
        "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    public static async Task<IResult> HandleAsync(
        Guid jobId,
        string formatKey,
        ISender sender,
        ICurrentTenant tenant,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not { } tenantId)
            return ProblemFor(OrganizationRequired);

        var result = await sender.Send(
            new ExportFormatToPptxQuery(jobId, formatKey, tenantId.Value),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.File(
                result.Value.PptxBytes,
                contentType: PptxMimeType,
                fileDownloadName: result.Value.DownloadFilename)
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
            title: error.Type switch
            {
                ErrorType.NotFound => "Resource not found",
                ErrorType.Conflict => "Conflict",
                _ => "Validation failed",
            },
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

    public FakeSender(Result<TResponse> result) => _result = result;

    public Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken cancellationToken = default)
    {
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
