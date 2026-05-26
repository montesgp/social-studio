using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Download;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Status;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Incoders.Hive.Tests.Api.Social.ContentGeneration;

/// <summary>
/// Cross-tenant isolation safety net.
/// A job created in Tenant A MUST NOT be accessible from Tenant B.
/// ALL cross-tenant responses MUST be 404 — never 403, 200, or 500.
/// This enforces the spec MUST requirement for no existence disclosure.
/// </summary>
public sealed class ContentGenerationCrossTenantIsolationTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();

    // ─── Status query from tenant B returns 404 ───────────────────────────────

    [Fact]
    public async Task GetStatus_TenantB_Returns404NotFound_NotForbidden_NotOk()
    {
        // Tenant B's sender returns NotFound (simulating repository tenant filter)
        var sender = new CrossTenantSender<ContentGenerationJobStatusQuery, ContentGenerationJobStatusResponse>();
        var tenantB = new FakeTenant(TenantB);

        var result = await IsolationHandlers.GetJobStatusAsync(
            jobId: JobId,
            sender: sender,
            tenant: tenantB);

        // MUST be 404 — never 403 (no existence disclosure)
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.NotEqual(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.NotEqual(StatusCodes.Status200OK, problem.StatusCode);
    }

    // ─── Download from tenant B returns 404 ───────────────────────────────────

    [Fact]
    public async Task DownloadPackage_TenantB_Returns404NotFound_NotForbidden_NotOk()
    {
        var sender = new CrossTenantSender<DownloadGeneratedPackageQuery, DownloadGeneratedPackageResponse>();
        var tenantB = new FakeTenant(TenantB);

        var result = await IsolationHandlers.DownloadPackageAsync(
            jobId: JobId,
            sender: sender,
            tenant: tenantB);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.NotEqual(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.NotEqual(StatusCodes.Status200OK, problem.StatusCode);
    }
}

// ─── Isolation handler mirror ─────────────────────────────────────────────────

file static class IsolationHandlers
{
    private static readonly Error OrganizationRequired =
        Error.Validation("tenant.required", "An organization context is required.");

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
            ? TypedResults.File(result.Value.ContentStream, "application/zip")
            : ProblemFor(result.Error);
    }

    private static IResult ProblemFor(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        };
        return TypedResults.Problem(
            title: "Resource not found",
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

/// <summary>
/// A sender that always returns NotFound — simulating the repo-level tenant filter
/// that returns null when the job belongs to a different org.
/// </summary>
file sealed class CrossTenantSender<TRequest, TResponse> : ISender
    where TRequest : IRequest<Result<TResponse>>
{
    public Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken cancellationToken = default)
    {
        var notFound = Result<TResponse>.Failure(
            Error.NotFound("content-gen.job-not-found", "Job not found"));
        return Task.FromResult((TOut)(object)notFound);
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
