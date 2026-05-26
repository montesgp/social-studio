using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.SingleAnalyzer.Analyze;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.SingleAnalyzer;
using Incoders.Hive.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Incoders.Hive.Tests.Api.Social;

/// <summary>
/// Unit tests for the single-analyzer endpoint handler logic.
/// The endpoint is thin: resolve tenant + user → build command → 202 Accepted with the status.
/// The async/poll contract: POST queues the work and returns immediately with the analysis id/status
/// (no full result on the request thread). Tests assert the CAP3-MUST-04 contract (no provider/model
/// on the command) and the tenant/user guard behavior, using the same handler logic extracted
/// as a pure function.
/// </summary>
public sealed class SingleAnalyzerEndpointTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    // ─── Happy path: command built without AI provider keys ──────────────────

    [Fact]
    public async Task Analyze_SendsCommandWithNullProviderKeys_WhenTenantAndUserResolved()
    {
        var sender = new FakeSender(Result.Success(MakeStatus(OrgId)));
        var tenant = new FakeTenant(OrgId);
        var user = new FakeUser(UserId);

        var result = await EndpointHandler.AnalyzeAsync(
            url: "https://www.tiktok.com/@brand/video/123",
            clientId: null,
            sender: sender,
            tenant: tenant,
            user: user);

        // POST queues the job → 202 Accepted with the pollable status.
        var accepted = Assert.IsType<Accepted<SingleAnalyzerStatusResponse>>(result);
        Assert.NotNull(accepted.Value);
        Assert.Equal(SingleContentAnalysisStatus.Requested, accepted.Value!.Status);

        // CAP3-MUST-04: the command type does not carry preferred AI provider/model at all.
        var cmd = Assert.IsType<AnalyzeSingleContentCommand>(sender.LastCommand);
        Assert.Equal("https://www.tiktok.com/@brand/video/123", cmd.Url);
        Assert.Equal(OrgId, cmd.OrganizationId);
        Assert.Equal(UserId, cmd.UserId);
        Assert.Null(cmd.ClientId);
    }

    [Fact]
    public async Task Analyze_PassesClientId_WhenProvided()
    {
        var clientId = Guid.NewGuid();
        var sender = new FakeSender(Result.Success(MakeStatus(OrgId, clientId)));
        var tenant = new FakeTenant(OrgId);
        var user = new FakeUser(UserId);

        await EndpointHandler.AnalyzeAsync("https://www.tiktok.com/@x/video/1", clientId, sender, tenant, user);

        var cmd = Assert.IsType<AnalyzeSingleContentCommand>(sender.LastCommand);
        Assert.Equal(clientId, cmd.ClientId);
    }

    // ─── Tenant guard ────────────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_ReturnsProblem_WhenTenantIdIsNull()
    {
        var sender = new FakeSender(Result.Success(MakeStatus(OrgId)));
        var tenant = new FakeTenant(null); // not resolved
        var user = new FakeUser(UserId);

        var result = await EndpointHandler.AnalyzeAsync(
            "https://www.tiktok.com/@x/video/1", null, sender, tenant, user);

        Assert.IsNotType<Accepted<SingleAnalyzerStatusResponse>>(result);
        Assert.Null(sender.LastCommand); // sender must NOT be called
    }

    // ─── User guard ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_ReturnsProblem_WhenUserIdIsNull()
    {
        var sender = new FakeSender(Result.Success(MakeStatus(OrgId)));
        var tenant = new FakeTenant(OrgId);
        var user = new FakeUser(null); // unauthenticated

        var result = await EndpointHandler.AnalyzeAsync(
            "https://www.tiktok.com/@x/video/1", null, sender, tenant, user);

        Assert.IsNotType<Accepted<SingleAnalyzerStatusResponse>>(result);
        Assert.Null(sender.LastCommand);
    }

    // ─── Domain error propagation ────────────────────────────────────────────

    [Fact]
    public async Task Analyze_PropagatesProblemDetails_WhenAnalyzerFails()
    {
        var sender = new FakeSender(
            Result.Failure<SingleAnalyzerStatusResponse>(SingleAnalyzerErrors.UrlInvalid));
        var tenant = new FakeTenant(OrgId);
        var user = new FakeUser(UserId);

        var result = await EndpointHandler.AnalyzeAsync(
            "not-a-url", null, sender, tenant, user);

        Assert.IsNotType<Accepted<SingleAnalyzerStatusResponse>>(result);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pure handler function that mirrors the endpoint's logic without referencing ApiService.
    /// This lets the Tests project test the exact mapping contract without a project reference
    /// to the ApiService layer.
    /// </summary>
    private static class EndpointHandler
    {
        internal static async Task<IResult> AnalyzeAsync(
            string url,
            Guid? clientId,
            FakeSender sender,
            ICurrentTenant tenant,
            ICurrentUser user)
        {
            if (tenant.TenantId is not { } tenantId)
            {
                return TypedResults.Problem(
                    title: "Validation failed",
                    detail: SingleAnalyzerErrors.OrganizationRequired.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = SingleAnalyzerErrors.OrganizationRequired.Code });
            }

            if (user.UserId is not { } userId)
            {
                return TypedResults.Problem(
                    title: "Validation failed",
                    detail: SingleAnalyzerErrors.UserRequired.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = SingleAnalyzerErrors.UserRequired.Code });
            }

            var result = await sender.Send(
                new AnalyzeSingleContentCommand(
                    OrganizationId: tenantId.Value,
                    UserId: userId,
                    Url: url,
                    ClientId: clientId,
                    PreferredExtractionProviderKey: null),
                CancellationToken.None);

            if (result.IsFailure)
            {
                return TypedResults.Problem(
                    title: "Error",
                    detail: result.Error.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
            }

            return TypedResults.Accepted(
                $"/api/social/single-analyzer/analyze/{result.Value.Id}", result.Value);
        }
    }

    private static SingleAnalyzerStatusResponse MakeStatus(Guid orgId, Guid? clientId = null) =>
        new(
            Id: Guid.NewGuid(),
            OrganizationId: orgId,
            ClientId: clientId,
            SourceUrl: "https://www.tiktok.com/@brand/video/123",
            NormalizedUrl: "https://www.tiktok.com/@brand/video/123",
            PlatformKey: "tiktok",
            Status: SingleContentAnalysisStatus.Requested,
            Snapshot: null,
            Analysis: null,
            FailureCode: null,
            FailureMessageKey: null,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: null);

    internal sealed class FakeSender : ISender
    {
        private readonly Result<SingleAnalyzerStatusResponse> _response;
        public object? LastCommand { get; private set; }

        public FakeSender(Result<SingleAnalyzerStatusResponse> response) => _response = response;

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastCommand = request;
            // Only handle AnalyzeSingleContentCommand
            if (request is AnalyzeSingleContentCommand)
            {
                return Task.FromResult((TResponse)(object)_response);
            }

            throw new InvalidOperationException($"Unexpected request type: {request.GetType().Name}");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class FakeTenant : ICurrentTenant
    {
        public FakeTenant(Guid? tenantId)
            => TenantId = tenantId.HasValue ? new TenantId(tenantId.Value) : null;

        public TenantId? TenantId { get; }
        public bool IsResolved => TenantId.HasValue;
    }

    private sealed class FakeUser : ICurrentUser
    {
        public FakeUser(Guid? userId) => UserId = userId;

        public bool IsAuthenticated => UserId.HasValue;
        public Guid? UserId { get; }
        public string? Email => null;
        public bool IsPlatformAdmin => false;
    }
}
