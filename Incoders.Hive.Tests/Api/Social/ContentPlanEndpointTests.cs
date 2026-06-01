using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ContentPlans;
using Incoders.Hive.Application.Features.Social.ContentPlans.GetByPeriod;
using Incoders.Hive.Application.Features.Social.ContentPlans.ListByClient;
using Incoders.Hive.Application.Features.Social.ContentPlans.Upsert;
using Incoders.Hive.Application.Features.Social.GeneralStrategies.GetByClientId;
using Incoders.Hive.Application.Features.Social.GeneralStrategies.Upsert;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentPlans;
using Incoders.Hive.Domain.Social.GeneralStrategies;
using Incoders.Hive.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Incoders.Hive.Tests.Api.Social;

/// <summary>
/// Smoke tests for the Content Plan and General Strategy endpoint handler logic.
/// Tests use a pure-function <see cref="EndpointHandler"/> that mirrors the ApiService
/// endpoint implementation — this avoids a circular project reference while still
/// verifying tenant guard behavior, command construction, and HTTP result mapping.
///
/// These are smoke tests: they verify the HTTP contract (200/404/422/400), the tenant
/// guard fires correctly, and the correct command/query types are dispatched.
/// Business logic is tested in the Application lifecycle tests.
/// </summary>
public sealed class ContentPlanEndpointTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();

    // ─── Content Plan: List ───────────────────────────────────────────────────

    [Fact]
    public async Task ListContentPlans_ValidTenant_Returns200WithSummaries()
    {
        var summaries = new List<ContentPlanSummaryResponse>
        {
            MakeSummary(ClientId, 2026, 5),
            MakeSummary(ClientId, 2026, 6),
        };
        var sender = new FakeSender<IReadOnlyList<ContentPlanSummaryResponse>>(
            Result.Success<IReadOnlyList<ContentPlanSummaryResponse>>(summaries));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandler.ListContentPlansAsync(ClientId, tenant, sender);

        var ok = Assert.IsType<Ok<IReadOnlyList<ContentPlanSummaryResponse>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        var cmd = Assert.IsType<ListContentPlansQuery>(sender.LastRequest);
        Assert.Equal(OrgId, cmd.OrganizationId);
        Assert.Equal(ClientId, cmd.ClientId);
    }

    [Fact]
    public async Task ListContentPlans_NoTenant_ReturnsProblem()
    {
        var sender = new FakeSender<IReadOnlyList<ContentPlanSummaryResponse>>(
            Result.Success<IReadOnlyList<ContentPlanSummaryResponse>>([]));
        var tenant = new FakeTenant(null);

        var result = await EndpointHandler.ListContentPlansAsync(ClientId, tenant, sender);

        Assert.IsNotType<Ok<IReadOnlyList<ContentPlanSummaryResponse>>>(result);
        Assert.Null(sender.LastRequest); // sender must NOT be called
    }

    // ─── Content Plan: Get by period ──────────────────────────────────────────

    [Fact]
    public async Task GetContentPlan_ValidTenantAndPeriod_Returns200WithPlan()
    {
        var plan = MakeFullPlan(ClientId, 2026, 6);
        var sender = new FakeSender<ContentPlanResponse>(Result.Success(plan));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandler.GetContentPlanAsync(ClientId, 2026, 6, tenant, sender);

        var ok = Assert.IsType<Ok<ContentPlanResponse>>(result);
        Assert.Equal(2026, ok.Value!.Year);
        Assert.Equal(6, ok.Value.Month);
        var cmd = Assert.IsType<GetContentPlanQuery>(sender.LastRequest);
        Assert.Equal(ClientId, cmd.ClientId);
        Assert.Equal(OrgId, cmd.OrganizationId);
    }

    [Fact]
    public async Task GetContentPlan_NotFound_ReturnsProblem()
    {
        var sender = new FakeSender<ContentPlanResponse>(
            Result.Failure<ContentPlanResponse>(ContentPlanErrors.NotFound));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandler.GetContentPlanAsync(ClientId, 2025, 1, tenant, sender);

        Assert.IsNotType<Ok<ContentPlanResponse>>(result);
    }

    [Fact]
    public async Task GetContentPlan_NoTenant_ReturnsProblem()
    {
        var sender = new FakeSender<ContentPlanResponse>(
            Result.Success(MakeFullPlan(ClientId, 2026, 6)));
        var tenant = new FakeTenant(null);

        var result = await EndpointHandler.GetContentPlanAsync(ClientId, 2026, 6, tenant, sender);

        Assert.IsNotType<Ok<ContentPlanResponse>>(result);
        Assert.Null(sender.LastRequest);
    }

    // ─── Content Plan: Upsert ────────────────────────────────────────────────

    [Fact]
    public async Task UpsertContentPlan_ValidCommand_Returns200WithPlan()
    {
        var plan = MakeFullPlan(ClientId, 2026, 6);
        var sender = new FakeSender<ContentPlanResponse>(Result.Success(plan));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandler.UpsertContentPlanAsync(
            ClientId, 2026, 6,
            objectives: "Build community",
            kpis: ["1000 DMs"],
            contentStrategyText: null,
            slots: null,
            tenant, sender);

        var ok = Assert.IsType<Ok<ContentPlanResponse>>(result);
        Assert.NotNull(ok.Value);
        var cmd = Assert.IsType<UpsertContentPlanCommand>(sender.LastRequest);
        Assert.Equal("Build community", cmd.Objectives);
        Assert.Equal(OrgId, cmd.OrganizationId);
        Assert.Equal(2026, cmd.Year);
        Assert.Equal(6, cmd.Month);
    }

    [Fact]
    public async Task UpsertContentPlan_ValidationError_Returns422()
    {
        var sender = new FakeSender<ContentPlanResponse>(
            Result.Failure<ContentPlanResponse>(ContentPlanErrors.ObjectiveRequired));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandler.UpsertContentPlanAsync(
            ClientId, 2026, 6,
            objectives: "",
            kpis: null, contentStrategyText: null, slots: null,
            tenant, sender);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
    }

    [Fact]
    public async Task UpsertContentPlan_NoTenant_ReturnsProblem()
    {
        var sender = new FakeSender<ContentPlanResponse>(
            Result.Success(MakeFullPlan(ClientId, 2026, 6)));
        var tenant = new FakeTenant(null);

        var result = await EndpointHandler.UpsertContentPlanAsync(
            ClientId, 2026, 6,
            objectives: "some objective",
            kpis: null, contentStrategyText: null, slots: null,
            tenant, sender);

        Assert.IsNotType<Ok<ContentPlanResponse>>(result);
        Assert.Null(sender.LastRequest);
    }

    // ─── General Strategy: Get ────────────────────────────────────────────────

    [Fact]
    public async Task GetGeneralStrategy_ValidTenant_Returns200WithStrategy()
    {
        var strategy = MakeStrategyResponse(ClientId, OrgId);
        var sender = new FakeSender<GeneralStrategyResponse>(Result.Success(strategy));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandler.GetGeneralStrategyAsync(ClientId, tenant, sender);

        var ok = Assert.IsType<Ok<GeneralStrategyResponse>>(result);
        Assert.Equal(ClientId, ok.Value!.ClientId);
        var cmd = Assert.IsType<GetGeneralStrategyQuery>(sender.LastRequest);
        Assert.Equal(OrgId, cmd.OrganizationId);
    }

    [Fact]
    public async Task GetGeneralStrategy_NotFound_ReturnsProblem()
    {
        var sender = new FakeSender<GeneralStrategyResponse>(
            Result.Failure<GeneralStrategyResponse>(GeneralStrategyErrors.NotFound));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandler.GetGeneralStrategyAsync(ClientId, tenant, sender);

        Assert.IsNotType<Ok<GeneralStrategyResponse>>(result);
    }

    [Fact]
    public async Task GetGeneralStrategy_NoTenant_ReturnsProblem()
    {
        var sender = new FakeSender<GeneralStrategyResponse>(
            Result.Success(MakeStrategyResponse(ClientId, OrgId)));
        var tenant = new FakeTenant(null);

        var result = await EndpointHandler.GetGeneralStrategyAsync(ClientId, tenant, sender);

        Assert.IsNotType<Ok<GeneralStrategyResponse>>(result);
        Assert.Null(sender.LastRequest);
    }

    // ─── General Strategy: Upsert ─────────────────────────────────────────────

    [Fact]
    public async Task UpsertGeneralStrategy_ValidCommand_Returns200WithStrategy()
    {
        var strategy = MakeStrategyResponse(ClientId, OrgId);
        var sender = new FakeSender<GeneralStrategyResponse>(Result.Success(strategy));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandler.UpsertGeneralStrategyAsync(
            ClientId,
            contentPillars: ["Edu"],
            toneKeywords: null,
            targetAudience: null,
            postingFrequency: null,
            notes: null,
            tenant, sender);

        var ok = Assert.IsType<Ok<GeneralStrategyResponse>>(result);
        Assert.NotNull(ok.Value);
        var cmd = Assert.IsType<UpsertGeneralStrategyCommand>(sender.LastRequest);
        Assert.Equal(OrgId, cmd.OrganizationId);
        Assert.Contains("Edu", cmd.ContentPillars!);
    }

    [Fact]
    public async Task UpsertGeneralStrategy_ValidationError_Returns422()
    {
        var sender = new FakeSender<GeneralStrategyResponse>(
            Result.Failure<GeneralStrategyResponse>(GeneralStrategyErrors.EmptyStrategy));
        var tenant = new FakeTenant(OrgId);

        var result = await EndpointHandler.UpsertGeneralStrategyAsync(
            ClientId,
            contentPillars: null, toneKeywords: null, targetAudience: null,
            postingFrequency: null, notes: null,
            tenant, sender);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, problem.StatusCode);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static ContentPlanSummaryResponse MakeSummary(Guid clientId, int year, int month) =>
        new(
            Id: Guid.NewGuid(),
            ClientId: clientId,
            PeriodKind: "Monthly",
            Year: year,
            Month: month,
            ObjectiveProvenance: new ProvenanceDto("Manual", "Draft", null, null),
            UpdatedAtUtc: DateTime.UtcNow);

    private static ContentPlanResponse MakeFullPlan(Guid clientId, int year, int month) =>
        new(
            Id: Guid.NewGuid(),
            ClientId: clientId,
            OrganizationId: OrgId,
            PeriodKind: "Monthly",
            Year: year,
            Month: month,
            ObjectiveText: "Some objective",
            Kpis: [],
            ObjectiveProvenance: new ProvenanceDto("Manual", "Draft", null, null),
            ContentStrategyText: null,
            ContentStrategyProvenance: new ProvenanceDto("Manual", "Empty", null, null),
            Slots: [],
            CalendarProvenance: new ProvenanceDto("Manual", "Empty", null, null),
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow);

    private static GeneralStrategyResponse MakeStrategyResponse(Guid clientId, Guid orgId) =>
        new(
            Id: Guid.NewGuid(),
            ClientId: clientId,
            OrganizationId: orgId,
            ContentPillars: ["Education"],
            ToneKeywords: [],
            TargetAudience: null,
            PostingFrequency: null,
            Notes: null,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow);

    // ─── Pure endpoint handler functions ─────────────────────────────────────
    //
    // These mirror the logic in ApiService endpoints exactly — enabling test coverage
    // without a project reference to ApiService. Keep in sync with the actual endpoint
    // implementations in Incoders.Hive.ApiService/Endpoints/Social/.

    private static class EndpointHandler
    {
        // ----- Content Plan endpoints -----

        internal static async Task<IResult> ListContentPlansAsync(
            Guid clientId,
            ICurrentTenant currentTenant,
            ISender sender)
        {
            if (currentTenant.TenantId is not { } organizationId)
            {
                return TypedResults.Problem(
                    title: "Not Found",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var result = await sender.Send(
                new ListContentPlansQuery(clientId, organizationId.Value),
                CancellationToken.None);

            return result.IsSuccess
                ? TypedResults.Ok(result.Value)
                : (IResult)TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest);
        }

        internal static async Task<IResult> GetContentPlanAsync(
            Guid clientId,
            int year,
            int month,
            ICurrentTenant currentTenant,
            ISender sender)
        {
            if (currentTenant.TenantId is not { } organizationId)
            {
                return TypedResults.Problem(
                    title: "Not Found",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var result = await sender.Send(
                new GetContentPlanQuery(clientId, organizationId.Value, year, month),
                CancellationToken.None);

            return result.IsSuccess
                ? TypedResults.Ok(result.Value)
                : (IResult)TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
        }

        internal static async Task<IResult> UpsertContentPlanAsync(
            Guid clientId,
            int year,
            int month,
            string objectives,
            IReadOnlyList<string>? kpis,
            string? contentStrategyText,
            IReadOnlyList<CalendarSlotRequest>? slots,
            ICurrentTenant currentTenant,
            ISender sender)
        {
            if (currentTenant.TenantId is not { } organizationId)
            {
                return TypedResults.Problem(
                    title: "Not Found",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var result = await sender.Send(
                new UpsertContentPlanCommand(
                    clientId, organizationId.Value,
                    year, month,
                    objectives, kpis, contentStrategyText, slots),
                CancellationToken.None);

            if (result.IsSuccess)
            {
                return TypedResults.Ok(result.Value);
            }

            return MapError(result.Error);
        }

        // ----- General Strategy endpoints -----

        internal static async Task<IResult> GetGeneralStrategyAsync(
            Guid clientId,
            ICurrentTenant currentTenant,
            ISender sender)
        {
            if (currentTenant.TenantId is not { } organizationId)
            {
                return TypedResults.Problem(
                    title: "Not Found",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var result = await sender.Send(
                new GetGeneralStrategyQuery(clientId, organizationId.Value),
                CancellationToken.None);

            return result.IsSuccess
                ? TypedResults.Ok(result.Value)
                : (IResult)TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);
        }

        internal static async Task<IResult> UpsertGeneralStrategyAsync(
            Guid clientId,
            IReadOnlyList<string>? contentPillars,
            IReadOnlyList<string>? toneKeywords,
            string? targetAudience,
            string? postingFrequency,
            string? notes,
            ICurrentTenant currentTenant,
            ISender sender)
        {
            if (currentTenant.TenantId is not { } organizationId)
            {
                return TypedResults.Problem(
                    title: "Not Found",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var result = await sender.Send(
                new UpsertGeneralStrategyCommand(
                    clientId, organizationId.Value,
                    contentPillars, toneKeywords,
                    targetAudience, postingFrequency, notes),
                CancellationToken.None);

            if (result.IsSuccess)
            {
                return TypedResults.Ok(result.Value);
            }

            return MapError(result.Error);
        }

        private static IResult MapError(Error error)
        {
            if (error.Type == ErrorType.Validation)
            {
                return TypedResults.Problem(
                    title: "Unprocessable Entity",
                    detail: error.MessageKey,
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    extensions: new Dictionary<string, object?> { ["code"] = error.Code });
            }

            return TypedResults.Problem(
                title: "Error",
                detail: error.MessageKey,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });
        }
    }

    // ─── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeSender<TResponse> : ISender
    {
        private readonly Result<TResponse> _response;
        public object? LastRequest { get; private set; }

        public FakeSender(Result<TResponse> response) => _response = response;

        public Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            // MediatR dispatches IRequest<Result<T>>, so TResult is Result<TResponse>
            if (typeof(TResult) == typeof(Result<TResponse>))
            {
                return Task.FromResult((TResult)(object)_response);
            }

            throw new InvalidOperationException(
                $"Unexpected request result type: {typeof(TResult).Name}, expected Result<{typeof(TResponse).Name}>");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);

        public IAsyncEnumerable<TResult> CreateStream<TResult>(
            IStreamRequest<TResult> request,
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
}
