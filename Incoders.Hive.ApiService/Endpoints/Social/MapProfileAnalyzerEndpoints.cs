using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts.GetByClient;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Dashboard;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.History;
using GetProfileAnalysisQuery = Incoders.Hive.Application.Features.Social.ProfileAnalyzer.History.GetProfileAnalysisQuery;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Impute;
using Incoders.Hive.Domain.Social.ProfileAnalysis;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.Social;

/// <summary>
/// ProfileAnalyzer endpoint group: /api/social/profile-analyzer
///
/// All endpoints are org-scoped — the organizationId is ALWAYS resolved from
/// <see cref="ICurrentTenant"/>, never from the request body or URL path.
/// Uses problem-details for all errors via <see cref="ResultExtensions"/>.
///
/// Spec IDs: PA-01–PA-03, PA-15, PA-20, PA-22
/// </summary>
public static class ProfileAnalyzerEndpointsExtensions
{
    public static IEndpointRouteBuilder MapProfileAnalyzerEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/profile-analyzer")
            .WithTags("Social Profile Analyzer")
            .RequireAuthorization();

        // Start a profile analysis (client+own-account path or URL path).
        // Returns 202 Accepted + id for async polling (PA-15).
        group.MapPost("/analyze", ProfileAnalyzerEndpointFacade.AnalyzeAsync)
            .WithName("AnalyzeProfile")
            .WithSummary("Queue a new profile analysis (client+own-account path or URL path)")
            .Produces<AnalyzeProfileStatusResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Poll: returns the current status/result of a profile analysis.
        group.MapGet("/analyze/{id:guid}", ProfileAnalyzerEndpointFacade.GetAnalysisAsync)
            .WithName("GetProfileAnalysis")
            .WithSummary("Get current status (or result) of a profile analysis — used for polling")
            .Produces<AnalyzeProfileStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Dashboard: full projection including stats + raw posts + comparison-if-client.
        group.MapGet("/analyze/{id:guid}/dashboard", ProfileAnalyzerEndpointFacade.GetDashboardAsync)
            .WithName("GetProfileAnalysisDashboard")
            .WithSummary("Get full dashboard for a completed profile analysis (PA-16, PA-17, PA-18)")
            .Produces<ProfileDashboardResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Impute: apply AI-detected fields to the linked client's data.
        group.MapPost("/analyze/{id:guid}/impute", ProfileAnalyzerEndpointFacade.ImputeAsync)
            .WithName("ImputeProfileAnalysis")
            .WithSummary("Impute AI-detected fields into the linked client's BrandManual/ContentPlan/GeneralStrategy (PA-20–PA-22)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // History: paged list of all profile analyses for the current org.
        group.MapGet("/analyze", ProfileAnalyzerEndpointFacade.ListAsync)
            .WithName("ListProfileAnalyses")
            .WithSummary("List profile analyses for the active organization (paged, newest first)")
            .Produces<ProfileAnalysisHistoryResponse>(StatusCodes.Status200OK);

        // Delete: hard-delete a profile analysis, org-scoped.
        group.MapDelete("/analyze/{id:guid}", ProfileAnalyzerEndpointFacade.DeleteAsync)
            .WithName("DeleteProfileAnalysis")
            .WithSummary("Hard-delete a profile analysis owned by the current organization")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}

/// <summary>
/// Internal facade exposing the ProfileAnalyzer endpoint handlers as static methods so
/// unit tests can call them directly without standing up a full WebApplication host.
/// Mirrors the <see cref="SingleAnalyzerEndpointFacade"/> pattern.
/// </summary>
internal static class ProfileAnalyzerEndpointFacade
{
    internal static async Task<IResult> AnalyzeAsync(
        [FromBody] AnalyzeProfileHttpRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ProfileAnalysisErrors.OrganizationRequired.ToProblem();

        if (currentUser.UserId is not { } userId)
            return ProfileAnalysisErrors.OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new AnalyzeProfileCommand(
                OrganizationId: tenantId.Value,
                UserId: userId,
                ClientId: body.ClientId,
                OwnAccountId: body.OwnAccountId,
                ProfileUrl: body.ProfileUrl),
            cancellationToken);

        return result.Match(status =>
            (IResult)TypedResults.Accepted(
                $"/api/social/profile-analyzer/analyze/{status.Id}", status));
    }

    internal static async Task<IResult> GetAnalysisAsync(
        Guid id,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ProfileAnalysisErrors.OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new GetProfileAnalysisQuery(id, tenantId.Value),
            cancellationToken);

        return result.Match(status => (IResult)TypedResults.Ok(status));
    }

    internal static async Task<IResult> GetDashboardAsync(
        Guid id,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ProfileAnalysisErrors.OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new GetProfileDashboardQuery(id, tenantId.Value),
            cancellationToken);

        return result.Match(dashboard => (IResult)TypedResults.Ok(dashboard));
    }

    internal static async Task<IResult> ImputeAsync(
        Guid id,
        [FromBody] ImputeProfileAnalysisRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ProfileAnalysisErrors.OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new ImputeProfileAnalysisCommand(id, tenantId.Value, body.Targets),
            cancellationToken);

        return result.Match(_ => (IResult)TypedResults.Ok());
    }

    internal static async Task<IResult> ListAsync(
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ProfileAnalysisErrors.OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new ListProfileAnalysesQuery(
                OrganizationId: tenantId.Value,
                Page: page ?? 1,
                PageSize: pageSize ?? 20),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    internal static async Task<IResult> DeleteAsync(
        Guid id,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } tenantId)
            return ProfileAnalysisErrors.OrganizationRequired.ToProblem();

        var result = await sender.Send(
            new DeleteProfileAnalysisCommand(id, tenantId.Value),
            cancellationToken);

        return result.Match(() => (IResult)TypedResults.NoContent());
    }
}

/// <summary>
/// Own-account picker endpoint: lists a client's own accounts filtered by network code.
/// This is wired as an ADDITION to the existing <see cref="ClientOwnAccountsEndpointsExtensions"/>
/// list endpoint — the existing ?network query param approach ensures PROPIAS (own accounts)
/// are returned only, filtered client-side after the MediatR query.
///
/// Route: GET /api/social/clients/{clientId}/own-accounts?network=instagram
/// (Handled in MapClientOwnAccountsEndpoints via the existing ListAsync — add the
/// [FromQuery] string? network parameter there and apply the filter.
/// See <see cref="ProfileAnalyzerOwnAccountPickerExtensions"/> for the registration note.)
/// </summary>
/// <remarks>
/// The own-account picker does NOT get a separate endpoint — it reuses the existing
/// GET /api/social/clients/{clientId}/own-accounts route with an added ?network= query parameter.
/// The network filter is applied at the endpoint layer (post-query, before the 200 response).
/// This keeps Application and Domain layers clean (no new query/handler needed).
/// </remarks>
public static class ProfileAnalyzerOwnAccountPickerNote
{
    // Implementation note: The ?network query param is handled by updating
    // MapClientOwnAccountsEndpoints.ListAsync — see the patch in that file.
}

/// <summary>
/// Request DTO for the profile analysis start endpoint.
/// Exactly one of (ClientId + OwnAccountId) or ProfileUrl must be supplied (PA-01, PA-03).
/// The OrganizationId is NEVER caller-selectable — it comes from the authenticated tenant context.
/// </summary>
public sealed record AnalyzeProfileHttpRequest(
    /// <summary>Client-path: the client linked to this analysis. Null for URL-path.</summary>
    Guid? ClientId,
    /// <summary>Client-path: the own Instagram account to analyze. Null for URL-path.</summary>
    Guid? OwnAccountId,
    /// <summary>URL-path: the Instagram profile URL. Null for client-path.</summary>
    string? ProfileUrl);
