using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.Social;

/// <summary>
/// NicheDiscovery endpoint group: /api/social/niche-discovery (design §10).
///
/// All run/opportunity endpoints are org-scoped from <see cref="ICurrentTenant"/>, never from the
/// request body or URL path. <c>/markets</c> is the one route with no tenant scope — the seeded market
/// catalog is a platform-wide table, not an organization resource (ND-14: the market selector is
/// data-driven by construction).
/// </summary>
public static class NicheDiscoveryEndpointsExtensions
{
    public static IEndpointRouteBuilder MapNicheDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/niche-discovery")
            .WithTags("Social Niche Discovery")
            .RequireAuthorization();

        group.MapPost("/runs", NicheDiscoveryEndpointFacade.StartRunAsync)
            .WithName("StartDiscoveryRun")
            .WithSummary("Queue a new niche discovery run (start-and-poll)")
            .Produces<StartDiscoveryRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/runs/{runId:guid}", NicheDiscoveryEndpointFacade.GetRunAsync)
            .WithName("GetDiscoveryRun")
            .WithSummary("Poll the current status of a discovery run")
            .Produces<DiscoveryRunStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/runs/{runId:guid}/opportunities", NicheDiscoveryEndpointFacade.ListRunOpportunitiesAsync)
            .WithName("ListRunOpportunities")
            .WithSummary("List the ranked opportunities produced by a completed discovery run")
            .Produces<RunOpportunitiesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/opportunities/{opportunityId:guid}", NicheDiscoveryEndpointFacade.GetOpportunityDetailAsync)
            .WithName("GetOpportunityDetail")
            .WithSummary("Get one opportunity's detail plus its navigable channel/video evidence")
            .Produces<OpportunityDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/runs", NicheDiscoveryEndpointFacade.ListRunsAsync)
            .WithName("ListDiscoveryRuns")
            .WithSummary("List this organization's discovery-run history, newest first")
            .Produces<DiscoveryRunHistoryResponse>(StatusCodes.Status200OK);

        group.MapGet("/markets", NicheDiscoveryEndpointFacade.ListMarketsAsync)
            .WithName("ListNicheDiscoveryMarkets")
            .WithSummary("List the seeded target-market catalog — the market selector is data-driven by construction")
            .Produces<MarketCatalogResponse>(StatusCodes.Status200OK);

        return app;
    }
}

/// <summary>
/// Internal facade exposing the endpoint handlers as static methods so tests can call them directly
/// without standing up a full WebApplication host. Mirrors <c>ProfileAnalyzerEndpointFacade</c>.
/// </summary>
internal static class NicheDiscoveryEndpointFacade
{
    internal static async Task<IResult> StartRunAsync(
        [FromBody] StartDiscoveryRunHttpRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return NicheDiscoveryErrors.OrganizationRequired.ToProblem();
        }

        if (currentUser.UserId is not { } userId)
        {
            return NicheDiscoveryErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new StartDiscoveryRunCommand(
                tenantId.Value,
                userId,
                body.SourceMarketCode,
                body.TargetMarketCode,
                body.NetworkKey,
                body.ContentFormat,
                body.WindowDays,
                body.Depth,
                body.NicheId,
                body.SeedTerms ?? [],
                body.Weights),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Accepted(
            $"/api/social/niche-discovery/runs/{response.RunId}", response));
    }

    internal static async Task<IResult> GetRunAsync(
        Guid runId, ISender sender, ICurrentTenant currentTenant, CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return NicheDiscoveryErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(new GetDiscoveryRunQuery(tenantId.Value, runId), cancellationToken);

        return result.Match(status => (IResult)TypedResults.Ok(status));
    }

    internal static async Task<IResult> ListRunOpportunitiesAsync(
        Guid runId, ISender sender, ICurrentTenant currentTenant, CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return NicheDiscoveryErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(new ListRunOpportunitiesQuery(tenantId.Value, runId), cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    internal static async Task<IResult> GetOpportunityDetailAsync(
        Guid opportunityId, ISender sender, ICurrentTenant currentTenant, CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return NicheDiscoveryErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(new GetOpportunityDetailQuery(tenantId.Value, opportunityId), cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    internal static async Task<IResult> ListRunsAsync(
        ISender sender,
        ICurrentTenant currentTenant,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
        {
            return NicheDiscoveryErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new ListDiscoveryRunsQuery(tenantId.Value, limit ?? ListDiscoveryRunsHandler.DefaultLimit),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    internal static async Task<IResult> ListMarketsAsync(IMarketCatalogRepository marketCatalog, CancellationToken cancellationToken)
    {
        var markets = await marketCatalog.ListActiveAsync(cancellationToken);

        return TypedResults.Ok(new MarketCatalogResponse(markets
            .Select(m => new MarketOption(m.Code.Value, m.DisplayNameKey, m.PrimaryLanguage))
            .ToList()));
    }
}

/// <summary>Request DTO for the discovery-run start endpoint (design §10). OrganizationId/UserId are
/// NEVER caller-selectable — both come from the authenticated tenant/user context.</summary>
public sealed record StartDiscoveryRunHttpRequest(
    string SourceMarketCode,
    string TargetMarketCode,
    string NetworkKey,
    string ContentFormat,
    int? WindowDays,
    string? Depth,
    Guid? NicheId,
    IReadOnlyList<string>? SeedTerms,
    Application.Features.Social.NicheDiscovery.Scoring.OpportunityScoreWeights? Weights);

public sealed record MarketOption(string Code, string DisplayNameKey, string PrimaryLanguage);

public sealed record MarketCatalogResponse(IReadOnlyList<MarketOption> Markets);
