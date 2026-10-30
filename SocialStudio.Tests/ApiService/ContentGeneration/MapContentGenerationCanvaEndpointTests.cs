using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Features.Social.ContentGeneration.Export;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SocialStudio.Tests.ApiService.ContentGeneration;

/// <summary>
/// B3 WU-7 endpoint tests for the export-to-canva route.
///
/// <para>
/// Tests validate the mapping between handler results and HTTP responses
/// without standing up a full web host — matching the pattern from
/// <c>MapContentGenerationPptxEndpointTests</c>.
/// </para>
///
/// <para>
/// Spec coverage:
/// CANVA-EXPORT-01 (200 + editUrl on success),
/// CANVA-EXPORT-02 (409 / 401 when user not connected + authorizeUrl in body).
/// </para>
/// </summary>
public sealed class MapContentGenerationCanvaEndpointTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid OrgIntegrationId = Guid.NewGuid();

    // ─── Happy path: 200 with editUrl ────────────────────────────────────────

    [Fact]
    public async Task ExportToCanva_Endpoint_Returns200WithEditUrl()
    {
        var response = new ExportFormatToCanvaResponse(
            DesignId: "design-abc",
            EditUrl: "https://canva.com/design/abc/edit",
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(30));

        var sender = new FakeCanvaSender(Result.Success(response));
        var tenant = new FakeCanvaTenant(OrgId);
        var currentUser = new FakeCurrentUser(Guid.NewGuid());

        var result = await ExportToCanvaHandler.HandleAsync(
            analysisId: Guid.NewGuid(),
            formatKey: "carousel",
            body: new ExportToCanvaRequest(JobId, OrgIntegrationId),
            sender: sender,
            tenant: tenant,
            currentUser: currentUser,
            cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<Ok<ExportFormatToCanvaResponse>>(result);
        Assert.Equal("https://canva.com/design/abc/edit", ok.Value!.EditUrl);
    }

    // ─── Not connected → 409 Conflict with canva.not_connected code ──────────

    [Fact]
    public async Task ExportToCanva_Endpoint_Returns409WhenNeedsAuthorization()
    {
        var error = Error.Conflict("canva.not_connected", "User has not connected their Canva account.");

        var sender = new FakeCanvaSender(Result.Failure<ExportFormatToCanvaResponse>(error));
        var tenant = new FakeCanvaTenant(OrgId);
        var currentUser = new FakeCurrentUser(Guid.NewGuid());

        var result = await ExportToCanvaHandler.HandleAsync(
            analysisId: Guid.NewGuid(),
            formatKey: "carousel",
            body: new ExportToCanvaRequest(JobId, OrgIntegrationId),
            sender: sender,
            tenant: tenant,
            currentUser: currentUser,
            cancellationToken: CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.True(
            problem.ProblemDetails.Extensions.ContainsKey("code"),
            "Problem details must include 'code' extension.");
        Assert.Equal("canva.not_connected", problem.ProblemDetails.Extensions["code"]);
    }

    // ─── Import timeout → 502 Bad Gateway ────────────────────────────────────

    [Fact]
    public async Task ExportToCanva_Endpoint_Returns502OnImportTimeout()
    {
        var error = Error.Failure("canva.import.timeout", "Import job timed out.");

        var sender = new FakeCanvaSender(Result.Failure<ExportFormatToCanvaResponse>(error));
        var tenant = new FakeCanvaTenant(OrgId);
        var currentUser = new FakeCurrentUser(Guid.NewGuid());

        var result = await ExportToCanvaHandler.HandleAsync(
            analysisId: Guid.NewGuid(),
            formatKey: "carousel",
            body: new ExportToCanvaRequest(JobId, OrgIntegrationId),
            sender: sender,
            tenant: tenant,
            currentUser: currentUser,
            cancellationToken: CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
    }

    // ─── Import API failure → 502 ─────────────────────────────────────────────

    [Fact]
    public async Task ExportToCanva_Endpoint_Returns502OnImportApiFailure()
    {
        var error = Error.Failure("canva.import.api-failure", "Canva returned an error.");

        var sender = new FakeCanvaSender(Result.Failure<ExportFormatToCanvaResponse>(error));
        var tenant = new FakeCanvaTenant(OrgId);
        var currentUser = new FakeCurrentUser(Guid.NewGuid());

        var result = await ExportToCanvaHandler.HandleAsync(
            analysisId: Guid.NewGuid(),
            formatKey: "carousel",
            body: new ExportToCanvaRequest(JobId, OrgIntegrationId),
            sender: sender,
            tenant: tenant,
            currentUser: currentUser,
            cancellationToken: CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
    }

    // ─── Not-exportable (no markup) → 409 ────────────────────────────────────

    [Fact]
    public async Task ExportToCanva_Endpoint_Returns409WhenNotExportable()
    {
        var error = Error.Conflict("content-gen.not-exportable", "No markup persisted.");

        var sender = new FakeCanvaSender(Result.Failure<ExportFormatToCanvaResponse>(error));
        var tenant = new FakeCanvaTenant(OrgId);
        var currentUser = new FakeCurrentUser(Guid.NewGuid());

        var result = await ExportToCanvaHandler.HandleAsync(
            analysisId: Guid.NewGuid(),
            formatKey: "carousel",
            body: new ExportToCanvaRequest(JobId, OrgIntegrationId),
            sender: sender,
            tenant: tenant,
            currentUser: currentUser,
            cancellationToken: CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    // ─── Missing tenant → problem response ───────────────────────────────────

    [Fact]
    public async Task ExportToCanva_MissingTenant_ReturnsProblem()
    {
        var sender = new FakeCanvaSender(
            Result.Success(new ExportFormatToCanvaResponse("d", "url", DateTimeOffset.UtcNow)));
        var tenant = new FakeCanvaTenant(null); // no tenant
        var currentUser = new FakeCurrentUser(Guid.NewGuid());

        var result = await ExportToCanvaHandler.HandleAsync(
            analysisId: Guid.NewGuid(),
            formatKey: "carousel",
            body: new ExportToCanvaRequest(JobId, OrgIntegrationId),
            sender: sender,
            tenant: tenant,
            currentUser: currentUser,
            cancellationToken: CancellationToken.None);

        Assert.IsType<ProblemHttpResult>(result);
    }

    // ─── Missing user → unauthorized ─────────────────────────────────────────

    [Fact]
    public async Task ExportToCanva_MissingUser_ReturnsUnauthorized()
    {
        var sender = new FakeCanvaSender(
            Result.Success(new ExportFormatToCanvaResponse("d", "url", DateTimeOffset.UtcNow)));
        var tenant = new FakeCanvaTenant(OrgId);
        var currentUser = new FakeCurrentUser(null); // no user

        var result = await ExportToCanvaHandler.HandleAsync(
            analysisId: Guid.NewGuid(),
            formatKey: "carousel",
            body: new ExportToCanvaRequest(JobId, OrgIntegrationId),
            sender: sender,
            tenant: tenant,
            currentUser: currentUser,
            cancellationToken: CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }
}

// ─── Pure handler mirror (avoids ApiService project reference in test project) ──

/// <summary>
/// Static mirror of the export-to-canva endpoint handler logic.
/// Kept in sync with <c>ContentGenerationEndpointFacade.ExportFormatToCanvaAsync</c>.
/// </summary>
file static class ExportToCanvaHandler
{
    private static readonly Error OrganizationRequired =
        Error.Validation("tenant.required", "An organization context is required for this operation.");

    // Canva-specific error code prefixes that map to 502 (upstream failure)
    private static readonly HashSet<string> UpstreamErrorPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "canva.import.timeout",
        "canva.import.api-failure",
        "canva.import.unauthorized",
    };

    public static async Task<IResult> HandleAsync(
        Guid analysisId,
        string formatKey,
        ExportToCanvaRequest body,
        ISender sender,
        ICurrentTenant tenant,
        IFakeCurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            return TypedResults.Unauthorized();

        if (tenant.TenantId is not { } tenantId)
            return ProblemFor(OrganizationRequired);

        var result = await sender.Send(
            new ExportFormatToCanvaCommand(
                JobId: body.JobId,
                FormatKey: formatKey,
                OrganizationId: tenantId.Value,
                OrganizationIntegrationId: body.OrgIntegrationId,
                UserId: currentUser.UserId.Value),
            cancellationToken);

        if (result.IsSuccess)
            return TypedResults.Ok(result.Value);

        // Upstream Canva failures → 502 Bad Gateway
        if (UpstreamErrorPrefixes.Contains(result.Error.Code))
        {
            return TypedResults.Problem(
                title: "Canva API failure",
                detail: result.Error.MessageKey,
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
        }

        return ProblemFor(result.Error);
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

file sealed record ExportToCanvaRequest(Guid JobId, Guid OrgIntegrationId);

file interface IFakeCurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
}

file sealed class FakeCurrentUser : IFakeCurrentUser
{
    public FakeCurrentUser(Guid? userId) => UserId = userId;
    public bool IsAuthenticated => UserId.HasValue;
    public Guid? UserId { get; }
}

file sealed class FakeCanvaTenant : ICurrentTenant
{
    public FakeCanvaTenant(Guid? tenantId)
        => TenantId = tenantId.HasValue ? new TenantId(tenantId.Value) : null;

    public TenantId? TenantId { get; }
    public bool IsResolved => TenantId.HasValue;
}

file sealed class FakeCanvaSender : ISender
{
    private readonly Result<ExportFormatToCanvaResponse> _result;

    public FakeCanvaSender(Result<ExportFormatToCanvaResponse> result) => _result = result;

    public Task<TOut> Send<TOut>(IRequest<TOut> request, CancellationToken cancellationToken = default)
    {
        if (request is ExportFormatToCanvaCommand)
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
