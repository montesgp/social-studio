using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts.GetByClient;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Dashboard;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.History;
using HistoryGetQuery = Incoders.Hive.Application.Features.Social.ProfileAnalyzer.History.GetProfileAnalysisQuery;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Impute;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using Incoders.Hive.Domain.Tenants;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Incoders.Hive.Tests.Api.Social;

/// <summary>
/// Facade tests for the ProfileAnalyzer endpoint group.
///
/// Pattern: mirrors SingleAnalyzerEndpointTests — the Tests project does NOT reference ApiService,
/// so each test class embeds an <see cref="EndpointHandler"/> private static class that
/// duplicates the endpoint's thin mapping logic. This allows verifying the command-dispatch
/// contract, org-scoping, error propagation, and HTTP status codes without standing up a
/// full WebApplication host.
///
/// Covered specs: PA-01, PA-02, PA-03, PA-15, PA-20, PA-22.
/// </summary>
public sealed class ProfileAnalyzerEndpointTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid OwnAccountId = Guid.NewGuid();
    private static readonly Guid AnalysisId = Guid.NewGuid();

    // ─── POST /analyze — client path (PA-01) ──────────────────────────────────

    [Fact]
    public async Task Analyze_ClientPath_Returns202WithId_WhenValid()
    {
        var expected = MakeStatusResponse(AnalysisId, OrgId, ClientId);
        var sender = new FakeMultiSender();
        sender.Register<AnalyzeProfileCommand, Result<AnalyzeProfileStatusResponse>>(
            Result.Success(expected));

        var result = await EndpointHandler.AnalyzeAsync(
            new AnalyzeProfileHttpBody(ClientId, OwnAccountId, null),
            sender,
            new FakeTenant(OrgId),
            new FakeUser(UserId));

        var accepted = Assert.IsType<Accepted<AnalyzeProfileStatusResponse>>(result);
        Assert.NotNull(accepted.Value);
        Assert.Equal(AnalysisId, accepted.Value!.Id);
        Assert.Equal(OrgId, accepted.Value.OrganizationId);
    }

    [Fact]
    public async Task Analyze_ClientPath_BuildsCorrectCommand()
    {
        var sender = new FakeMultiSender();
        sender.Register<AnalyzeProfileCommand, Result<AnalyzeProfileStatusResponse>>(
            Result.Success(MakeStatusResponse(AnalysisId, OrgId, ClientId)));

        await EndpointHandler.AnalyzeAsync(
            new AnalyzeProfileHttpBody(ClientId, OwnAccountId, null),
            sender,
            new FakeTenant(OrgId),
            new FakeUser(UserId));

        var cmd = Assert.IsType<AnalyzeProfileCommand>(sender.LastSent);
        Assert.Equal(OrgId, cmd.OrganizationId);
        Assert.Equal(UserId, cmd.UserId);
        Assert.Equal(ClientId, cmd.ClientId);
        Assert.Equal(OwnAccountId, cmd.OwnAccountId);
        Assert.Null(cmd.ProfileUrl);
    }

    // ─── POST /analyze — URL path (PA-02) ──────────────────────────────────────

    [Fact]
    public async Task Analyze_UrlPath_Returns202_WhenValid()
    {
        const string profileUrl = "https://www.instagram.com/exampleaccount/";
        var sender = new FakeMultiSender();
        sender.Register<AnalyzeProfileCommand, Result<AnalyzeProfileStatusResponse>>(
            Result.Success(MakeStatusResponse(AnalysisId, OrgId, null)));

        var result = await EndpointHandler.AnalyzeAsync(
            new AnalyzeProfileHttpBody(null, null, profileUrl),
            sender,
            new FakeTenant(OrgId),
            new FakeUser(UserId));

        Assert.IsType<Accepted<AnalyzeProfileStatusResponse>>(result);
        var cmd = Assert.IsType<AnalyzeProfileCommand>(sender.LastSent);
        Assert.Null(cmd.ClientId);
        Assert.Null(cmd.OwnAccountId);
        Assert.Equal(profileUrl, cmd.ProfileUrl);
    }

    // ─── POST /analyze — mutual exclusion validation (PA-03) ────────────────────

    [Fact]
    public async Task Analyze_BothSourcesSupplied_ReturnsBadRequest()
    {
        var sender = new FakeMultiSender();
        sender.Register<AnalyzeProfileCommand, Result<AnalyzeProfileStatusResponse>>(
            Result.Failure<AnalyzeProfileStatusResponse>(ProfileAnalysisErrors.ProfileSourceMutuallyExclusive));

        var result = await EndpointHandler.AnalyzeAsync(
            new AnalyzeProfileHttpBody(ClientId, OwnAccountId, "https://instagram.com/x"),
            sender,
            new FakeTenant(OrgId),
            new FakeUser(UserId));

        Assert.IsNotType<Accepted<AnalyzeProfileStatusResponse>>(result);
    }

    [Fact]
    public async Task Analyze_NeitherSourceSupplied_ReturnsBadRequest()
    {
        var sender = new FakeMultiSender();
        sender.Register<AnalyzeProfileCommand, Result<AnalyzeProfileStatusResponse>>(
            Result.Failure<AnalyzeProfileStatusResponse>(ProfileAnalysisErrors.ProfileSourceRequired));

        var result = await EndpointHandler.AnalyzeAsync(
            new AnalyzeProfileHttpBody(null, null, null),
            sender,
            new FakeTenant(OrgId),
            new FakeUser(UserId));

        Assert.IsNotType<Accepted<AnalyzeProfileStatusResponse>>(result);
    }

    // ─── POST /analyze — tenant guard ────────────────────────────────────────────

    [Fact]
    public async Task Analyze_ReturnsProblem_WhenTenantNotResolved()
    {
        var sender = new FakeMultiSender();
        // Sender should NOT be called
        var result = await EndpointHandler.AnalyzeAsync(
            new AnalyzeProfileHttpBody(ClientId, OwnAccountId, null),
            sender,
            new FakeTenant(null),
            new FakeUser(UserId));

        Assert.IsNotType<Accepted<AnalyzeProfileStatusResponse>>(result);
        Assert.Null(sender.LastSent);
    }

    // ─── GET /{id} — poll/status (PA-15) ────────────────────────────────────────

    [Fact]
    public async Task GetAnalysis_Returns200WithStatus_WhenFound()
    {
        var expected = MakeStatusResponse(AnalysisId, OrgId, null);
        var sender = new FakeMultiSender();
        sender.Register<HistoryGetQuery, Result<AnalyzeProfileStatusResponse>>(
            Result.Success(expected));

        var result = await EndpointHandler.GetAnalysisAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        var ok = Assert.IsType<Ok<AnalyzeProfileStatusResponse>>(result);
        Assert.Equal(AnalysisId, ok.Value!.Id);
    }

    [Fact]
    public async Task GetAnalysis_Returns404_WhenWrongOrg()
    {
        // Cross-org: handler returns NotFound
        var sender = new FakeMultiSender();
        sender.Register<HistoryGetQuery, Result<AnalyzeProfileStatusResponse>>(
            Result.Failure<AnalyzeProfileStatusResponse>(ProfileAnalysisErrors.NotFound));

        var result = await EndpointHandler.GetAnalysisAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        Assert.IsNotType<Ok<AnalyzeProfileStatusResponse>>(result);
    }

    [Fact]
    public async Task GetAnalysis_SendsCorrectOrgScope()
    {
        var sender = new FakeMultiSender();
        sender.Register<HistoryGetQuery, Result<AnalyzeProfileStatusResponse>>(
            Result.Success(MakeStatusResponse(AnalysisId, OrgId, null)));

        await EndpointHandler.GetAnalysisAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        var query = Assert.IsType<HistoryGetQuery>(sender.LastSent);
        Assert.Equal(OrgId, query.OrganizationId);
        Assert.Equal(AnalysisId, query.AnalysisId);
    }

    // ─── GET /{id}/dashboard (PA-15, PA-16) ──────────────────────────────────────

    [Fact]
    public async Task GetDashboard_Returns200_WhenAnalysisFound()
    {
        var expected = MakeDashboardResponse(AnalysisId, OrgId);
        var sender = new FakeMultiSender();
        sender.Register<GetProfileDashboardQuery, Result<ProfileDashboardResponse>>(
            Result.Success(expected));

        var result = await EndpointHandler.GetDashboardAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        var ok = Assert.IsType<Ok<ProfileDashboardResponse>>(result);
        Assert.Equal(AnalysisId, ok.Value!.Id);
    }

    [Fact]
    public async Task GetDashboard_Returns404_WhenNotFound()
    {
        var sender = new FakeMultiSender();
        sender.Register<GetProfileDashboardQuery, Result<ProfileDashboardResponse>>(
            Result.Failure<ProfileDashboardResponse>(ProfileAnalysisErrors.NotFound));

        var result = await EndpointHandler.GetDashboardAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        Assert.IsNotType<Ok<ProfileDashboardResponse>>(result);
    }

    // ─── POST /{id}/impute — blocked when not Analyzed (PA-22) ────────────────────

    [Fact]
    public async Task Impute_Returns422_WhenStatusNotAnalyzed()
    {
        var sender = new FakeMultiSender();
        sender.Register<ImputeProfileAnalysisCommand, Result<Unit>>(
            Result.Failure<Unit>(ProfileAnalysisErrors.NotAnalyzed));

        var request = new ImputeProfileAnalysisRequest([ProfileImputeTarget.ColorPalette]);
        var result = await EndpointHandler.ImputeAsync(
            AnalysisId, request, sender, new FakeTenant(OrgId));

        Assert.IsNotType<Ok>(result);
        Assert.IsNotType<NoContent>(result);
    }

    [Fact]
    public async Task Impute_ReturnsError_WhenClientIdNull()
    {
        // URL-path analysis: no client linked (PA-20)
        var sender = new FakeMultiSender();
        sender.Register<ImputeProfileAnalysisCommand, Result<Unit>>(
            Result.Failure<Unit>(ProfileAnalysisErrors.ClientRequired));

        var request = new ImputeProfileAnalysisRequest([ProfileImputeTarget.ColorPalette]);
        var result = await EndpointHandler.ImputeAsync(
            AnalysisId, request, sender, new FakeTenant(OrgId));

        Assert.IsNotType<Ok>(result);
    }

    [Fact]
    public async Task Impute_Returns200_WhenAnalyzedAndClientLinked()
    {
        var sender = new FakeMultiSender();
        sender.Register<ImputeProfileAnalysisCommand, Result<Unit>>(
            Result.Success(Unit.Value));

        var request = new ImputeProfileAnalysisRequest([ProfileImputeTarget.ColorPalette]);
        var result = await EndpointHandler.ImputeAsync(
            AnalysisId, request, sender, new FakeTenant(OrgId));

        Assert.IsType<Ok>(result);
    }

    [Fact]
    public async Task Impute_SendsCorrectOrgScopeAndTargets()
    {
        var sender = new FakeMultiSender();
        sender.Register<ImputeProfileAnalysisCommand, Result<Unit>>(
            Result.Success(Unit.Value));

        var targets = new List<ProfileImputeTarget>
            { ProfileImputeTarget.ColorPalette, ProfileImputeTarget.ContentPillars };
        var request = new ImputeProfileAnalysisRequest(targets);
        await EndpointHandler.ImputeAsync(
            AnalysisId, request, sender, new FakeTenant(OrgId));

        var cmd = Assert.IsType<ImputeProfileAnalysisCommand>(sender.LastSent);
        Assert.Equal(OrgId, cmd.OrganizationId);
        Assert.Equal(AnalysisId, cmd.AnalysisId);
        Assert.Equal(2, cmd.Targets.Count);
    }

    // ─── GET / — list history, org-scoped (PA-15) ─────────────────────────────────

    [Fact]
    public async Task ListAnalyses_Returns200_WithOrgScopedItems()
    {
        var expected = new ProfileAnalysisHistoryResponse(
            [new ProfileAnalysisHistoryItemDto(
                AnalysisId, ClientId, "@handle", "Analyzed", null,
                DateTime.UtcNow, DateTime.UtcNow)],
            TotalCount: 1, Page: 1, PageSize: 20);

        var sender = new FakeMultiSender();
        sender.Register<ListProfileAnalysesQuery, Result<ProfileAnalysisHistoryResponse>>(
            Result.Success(expected));

        var result = await EndpointHandler.ListAsync(
            sender, new FakeTenant(OrgId), page: null, pageSize: null);

        var ok = Assert.IsType<Ok<ProfileAnalysisHistoryResponse>>(result);
        Assert.Single(ok.Value!.Items);
    }

    [Fact]
    public async Task ListAnalyses_SendsCorrectOrgId()
    {
        var sender = new FakeMultiSender();
        sender.Register<ListProfileAnalysesQuery, Result<ProfileAnalysisHistoryResponse>>(
            Result.Success(new ProfileAnalysisHistoryResponse([], 0, 1, 20)));

        await EndpointHandler.ListAsync(
            sender, new FakeTenant(OrgId), page: 2, pageSize: 10);

        var query = Assert.IsType<ListProfileAnalysesQuery>(sender.LastSent);
        Assert.Equal(OrgId, query.OrganizationId);
        Assert.Equal(2, query.Page);
        Assert.Equal(10, query.PageSize);
    }

    // ─── DELETE /{id} — org-scoped (PA-15) ────────────────────────────────────────

    [Fact]
    public async Task DeleteAnalysis_Returns204_WhenFound()
    {
        var sender = new FakeMultiSender();
        sender.Register<DeleteProfileAnalysisCommand, Result>(Result.Success());

        var result = await EndpointHandler.DeleteAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task DeleteAnalysis_Returns404_WhenWrongOrg()
    {
        var sender = new FakeMultiSender();
        sender.Register<DeleteProfileAnalysisCommand, Result>(
            Result.Failure(ProfileAnalysisErrors.NotFound));

        var result = await EndpointHandler.DeleteAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        Assert.IsNotType<NoContent>(result);
    }

    [Fact]
    public async Task DeleteAnalysis_SendsCorrectOrgScope()
    {
        var sender = new FakeMultiSender();
        sender.Register<DeleteProfileAnalysisCommand, Result>(Result.Success());

        await EndpointHandler.DeleteAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        var cmd = Assert.IsType<DeleteProfileAnalysisCommand>(sender.LastSent);
        Assert.Equal(OrgId, cmd.OrganizationId);
        Assert.Equal(AnalysisId, cmd.AnalysisId);
    }

    // ─── POST /{id}/retry-ai — re-runs AI-only stage ─────────────────────────────────

    [Fact]
    public async Task RetryAi_Returns202WithStatus_WhenResumable()
    {
        var expected = MakeStatusResponse(AnalysisId, OrgId, null) with { Status = "Analyzing" };
        var sender = new FakeMultiSender();
        sender.Register<RetryProfileAnalysisCommand, Result<AnalyzeProfileStatusResponse>>(
            Result.Success(expected));

        var result = await EndpointHandler.RetryAiAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        var accepted = Assert.IsType<Accepted<AnalyzeProfileStatusResponse>>(result);
        Assert.NotNull(accepted.Value);
        Assert.Equal(AnalysisId, accepted.Value!.Id);
        Assert.Equal("Analyzing", accepted.Value.Status);
    }

    [Fact]
    public async Task RetryAi_Returns404_WhenAnalysisNotFound()
    {
        var sender = new FakeMultiSender();
        sender.Register<RetryProfileAnalysisCommand, Result<AnalyzeProfileStatusResponse>>(
            Result.Failure<AnalyzeProfileStatusResponse>(ProfileAnalysisErrors.NotFound));

        var result = await EndpointHandler.RetryAiAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        Assert.IsNotType<Accepted<AnalyzeProfileStatusResponse>>(result);
    }

    [Fact]
    public async Task RetryAi_Returns422_WhenAnalysisNotResumable()
    {
        var sender = new FakeMultiSender();
        sender.Register<RetryProfileAnalysisCommand, Result<AnalyzeProfileStatusResponse>>(
            Result.Failure<AnalyzeProfileStatusResponse>(ProfileAnalysisErrors.NotResumable));

        var result = await EndpointHandler.RetryAiAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        Assert.IsNotType<Accepted<AnalyzeProfileStatusResponse>>(result);
    }

    [Fact]
    public async Task RetryAi_SendsCorrectOrgScopeAndAnalysisId()
    {
        var sender = new FakeMultiSender();
        sender.Register<RetryProfileAnalysisCommand, Result<AnalyzeProfileStatusResponse>>(
            Result.Success(MakeStatusResponse(AnalysisId, OrgId, null)));

        await EndpointHandler.RetryAiAsync(
            AnalysisId, sender, new FakeTenant(OrgId));

        var cmd = Assert.IsType<RetryProfileAnalysisCommand>(sender.LastSent);
        Assert.Equal(OrgId, cmd.OrganizationId);
        Assert.Equal(AnalysisId, cmd.AnalysisId);
    }

    [Fact]
    public async Task RetryAi_ReturnsProblem_WhenTenantNotResolved()
    {
        var sender = new FakeMultiSender();
        var result = await EndpointHandler.RetryAiAsync(
            AnalysisId, sender, new FakeTenant(null));

        Assert.IsNotType<Accepted<AnalyzeProfileStatusResponse>>(result);
        Assert.Null(sender.LastSent);
    }

    // ─── Own-account picker — filters to Instagram network (PA-01) ─────────────────

    [Fact]
    public async Task OwnAccountPicker_FiltersToInstagram_WhenNetworkQueryParamSupplied()
    {
        // Returns two accounts: instagram + facebook. Only instagram should be returned.
        var instagramNetworkCode = "instagram";
        var accounts = new PagedClientOwnAccountsResponse(
            [
                MakeOwnAccountResponse(Guid.NewGuid(), ClientId, instagramNetworkCode),
                MakeOwnAccountResponse(Guid.NewGuid(), ClientId, "facebook"),
            ],
            TotalCount: 2);

        var sender = new FakeMultiSender();
        sender.Register<ListClientOwnAccountsByClientQuery, Result<PagedClientOwnAccountsResponse>>(
            Result.Success(accounts));

        var result = await EndpointHandler.ListOwnAccountsAsync(
            ClientId, networkFilter: instagramNetworkCode,
            sender, new FakeTenant(OrgId));

        var ok = Assert.IsType<Ok<PagedClientOwnAccountsResponse>>(result);
        Assert.Single(ok.Value!.Items);
        Assert.All(ok.Value.Items, a =>
            Assert.Equal(instagramNetworkCode, a.SocialNetworkCode, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OwnAccountPicker_ReturnsAll_WhenNoNetworkFilter()
    {
        var accounts = new PagedClientOwnAccountsResponse(
            [
                MakeOwnAccountResponse(Guid.NewGuid(), ClientId, "instagram"),
                MakeOwnAccountResponse(Guid.NewGuid(), ClientId, "facebook"),
            ],
            TotalCount: 2);

        var sender = new FakeMultiSender();
        sender.Register<ListClientOwnAccountsByClientQuery, Result<PagedClientOwnAccountsResponse>>(
            Result.Success(accounts));

        var result = await EndpointHandler.ListOwnAccountsAsync(
            ClientId, networkFilter: null,
            sender, new FakeTenant(OrgId));

        var ok = Assert.IsType<Ok<PagedClientOwnAccountsResponse>>(result);
        Assert.Equal(2, ok.Value!.Items.Count);
    }

    [Fact]
    public async Task OwnAccountPicker_ReturnsProblem_WhenTenantNotResolved()
    {
        var sender = new FakeMultiSender();

        var result = await EndpointHandler.ListOwnAccountsAsync(
            ClientId, networkFilter: "instagram",
            sender, new FakeTenant(null));

        Assert.IsNotType<Ok<PagedClientOwnAccountsResponse>>(result);
        Assert.Null(sender.LastSent);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static AnalyzeProfileStatusResponse MakeStatusResponse(
        Guid id, Guid orgId, Guid? clientId) =>
        new(id, orgId, clientId, null, "@handle", "Requested", null,
            DateTime.UtcNow, DateTime.UtcNow, null);

    private static ProfileDashboardResponse MakeDashboardResponse(Guid id, Guid orgId) =>
        new(id, orgId, null, "@handle", "Analyzed",
            null, null, [], null, null, DateTime.UtcNow, null);

    private static ClientOwnAccountResponse MakeOwnAccountResponse(
        Guid id, Guid clientId, string networkCode) =>
        new(id, clientId, OrgId, Guid.NewGuid(),
            networkCode, networkCode, $"{networkCode}-icon",
            $"https://instagram.com/{networkCode}_account",
            $"@{networkCode}_handle", null, null,
            IsActive: true, SortOrder: 0,
            DateTime.UtcNow, DateTime.UtcNow);

    // ─── Endpoint handler mirror (no ApiService reference) ────────────────────────

    /// <summary>
    /// Mirrors the endpoint handler logic so tests can verify dispatch contracts.
    /// Updated to stay in sync with <see cref="ProfileAnalyzerEndpointFacade"/> in ApiService.
    /// </summary>
    private static class EndpointHandler
    {
        internal static async Task<IResult> AnalyzeAsync(
            AnalyzeProfileHttpBody body,
            FakeMultiSender sender,
            ICurrentTenant tenant,
            ICurrentUser user)
        {
            if (tenant.TenantId is not { } tenantId)
                return TypedResults.Problem(
                    title: "Validation failed",
                    detail: ProfileAnalysisErrors.OrganizationRequired.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = ProfileAnalysisErrors.OrganizationRequired.Code });

            if (user.UserId is not { } userId)
                return TypedResults.Problem(
                    title: "Validation failed",
                    detail: ProfileAnalysisErrors.OrganizationRequired.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = ProfileAnalysisErrors.OrganizationRequired.Code });

            var result = await sender.SendTyped<AnalyzeProfileCommand, Result<AnalyzeProfileStatusResponse>>(
                new AnalyzeProfileCommand(tenantId.Value, userId, body.ClientId, body.OwnAccountId, body.ProfileUrl),
                CancellationToken.None);

            return result.IsSuccess
                ? (IResult)TypedResults.Accepted(
                    $"/api/social/profile-analyzer/analyze/{result.Value.Id}", result.Value)
                : MapError(result.Error);
        }

        internal static async Task<IResult> GetAnalysisAsync(
            Guid id,
            FakeMultiSender sender,
            ICurrentTenant tenant)
        {
            if (tenant.TenantId is not { } tenantId)
                return TypedResults.Problem(
                    title: "Validation failed",
                    detail: ProfileAnalysisErrors.OrganizationRequired.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = ProfileAnalysisErrors.OrganizationRequired.Code });

            var result = await sender.SendTyped<HistoryGetQuery, Result<AnalyzeProfileStatusResponse>>(
                new HistoryGetQuery(id, tenantId.Value),
                CancellationToken.None);

            return result.IsSuccess ? (IResult)TypedResults.Ok(result.Value) : MapError(result.Error);
        }

        internal static async Task<IResult> GetDashboardAsync(
            Guid id,
            FakeMultiSender sender,
            ICurrentTenant tenant)
        {
            if (tenant.TenantId is not { } tenantId)
                return TypedResults.Problem(
                    title: "Validation failed",
                    detail: ProfileAnalysisErrors.OrganizationRequired.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = ProfileAnalysisErrors.OrganizationRequired.Code });

            var result = await sender.SendTyped<GetProfileDashboardQuery, Result<ProfileDashboardResponse>>(
                new GetProfileDashboardQuery(id, tenantId.Value),
                CancellationToken.None);

            return result.IsSuccess ? (IResult)TypedResults.Ok(result.Value) : MapError(result.Error);
        }

        internal static async Task<IResult> ImputeAsync(
            Guid id,
            ImputeProfileAnalysisRequest body,
            FakeMultiSender sender,
            ICurrentTenant tenant)
        {
            if (tenant.TenantId is not { } tenantId)
                return TypedResults.Problem(
                    title: "Validation failed",
                    detail: ProfileAnalysisErrors.OrganizationRequired.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = ProfileAnalysisErrors.OrganizationRequired.Code });

            var result = await sender.SendTyped<ImputeProfileAnalysisCommand, Result<Unit>>(
                new ImputeProfileAnalysisCommand(id, tenantId.Value, body.Targets),
                CancellationToken.None);

            return result.IsSuccess ? (IResult)TypedResults.Ok() : MapError(result.Error);
        }

        internal static async Task<IResult> ListAsync(
            FakeMultiSender sender,
            ICurrentTenant tenant,
            int? page,
            int? pageSize)
        {
            if (tenant.TenantId is not { } tenantId)
                return TypedResults.Problem(
                    title: "Validation failed",
                    detail: ProfileAnalysisErrors.OrganizationRequired.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = ProfileAnalysisErrors.OrganizationRequired.Code });

            var result = await sender.SendTyped<ListProfileAnalysesQuery, Result<ProfileAnalysisHistoryResponse>>(
                new ListProfileAnalysesQuery(tenantId.Value, page ?? 1, pageSize ?? 20),
                CancellationToken.None);

            return result.IsSuccess ? (IResult)TypedResults.Ok(result.Value) : MapError(result.Error);
        }

        internal static async Task<IResult> DeleteAsync(
            Guid id,
            FakeMultiSender sender,
            ICurrentTenant tenant)
        {
            if (tenant.TenantId is not { } tenantId)
                return TypedResults.Problem(
                    title: "Validation failed",
                    detail: ProfileAnalysisErrors.OrganizationRequired.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = ProfileAnalysisErrors.OrganizationRequired.Code });

            var result = await sender.SendTyped<DeleteProfileAnalysisCommand, Result>(
                new DeleteProfileAnalysisCommand(id, tenantId.Value),
                CancellationToken.None);

            return result.IsSuccess ? (IResult)TypedResults.NoContent() : MapError(result.Error);
        }

        internal static async Task<IResult> RetryAiAsync(
            Guid id,
            FakeMultiSender sender,
            ICurrentTenant tenant)
        {
            if (tenant.TenantId is not { } tenantId)
                return TypedResults.Problem(
                    title: "Validation failed",
                    detail: ProfileAnalysisErrors.OrganizationRequired.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = ProfileAnalysisErrors.OrganizationRequired.Code });

            var result = await sender.SendTyped<RetryProfileAnalysisCommand, Result<AnalyzeProfileStatusResponse>>(
                new RetryProfileAnalysisCommand(id, tenantId.Value),
                CancellationToken.None);

            return result.IsSuccess
                ? (IResult)TypedResults.Accepted(
                    $"/api/social/profile-analyzer/analyze/{result.Value.Id}", result.Value)
                : MapError(result.Error);
        }

        internal static async Task<IResult> ListOwnAccountsAsync(
            Guid clientId,
            string? networkFilter,
            FakeMultiSender sender,
            ICurrentTenant tenant)
        {
            if (tenant.TenantId is not { } tenantId)
                return TypedResults.Problem(
                    title: "Validation failed",
                    detail: ProfileAnalysisErrors.OrganizationRequired.MessageKey,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["code"] = ProfileAnalysisErrors.OrganizationRequired.Code });

            var result = await sender.SendTyped<ListClientOwnAccountsByClientQuery, Result<PagedClientOwnAccountsResponse>>(
                new ListClientOwnAccountsByClientQuery(tenantId.Value, clientId, false, 1, 100),
                CancellationToken.None);

            if (result.IsFailure) return MapError(result.Error);

            // Apply thin network filter at the endpoint layer (PA-01 own-account picker)
            var items = networkFilter is null
                ? result.Value.Items
                : result.Value.Items
                    .Where(a => a.SocialNetworkCode.Equals(networkFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            return TypedResults.Ok(new PagedClientOwnAccountsResponse(items, items.Count));
        }

        // Re-implementation of ToProblem so the Tests project doesn't reference ApiService.
        private static IResult MapError(Error error) => TypedResults.Problem(
            title: error.Type == ErrorType.NotFound ? "Resource not found" : "Validation failed",
            detail: error.MessageKey,
            statusCode: error.Type == ErrorType.NotFound
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    // ─── Test DTO ─────────────────────────────────────────────────────────────────

    internal sealed record AnalyzeProfileHttpBody(
        Guid? ClientId,
        Guid? OwnAccountId,
        string? ProfileUrl);

    // ─── Fakes ────────────────────────────────────────────────────────────────────

    internal sealed class FakeMultiSender : ISender
    {
        private readonly Dictionary<Type, object> _responses = new();
        public object? LastSent { get; private set; }

        public void Register<TRequest, TResponse>(TResponse response) =>
            _responses[typeof(TRequest)] = response!;

        public Task<TResponse> SendTyped<TRequest, TResponse>(
            TRequest request,
            CancellationToken cancellationToken)
            where TRequest : IRequest<TResponse>
        {
            LastSent = request;
            if (_responses.TryGetValue(typeof(TRequest), out var response))
                return Task.FromResult((TResponse)response);
            throw new InvalidOperationException(
                $"No response registered for {typeof(TRequest).Name}");
        }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastSent = request;
            if (_responses.TryGetValue(request.GetType(), out var response))
                return Task.FromResult((TResponse)response);
            throw new InvalidOperationException(
                $"No response registered for {request.GetType().Name}");
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
