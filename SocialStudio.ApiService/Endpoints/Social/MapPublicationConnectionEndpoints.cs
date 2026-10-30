using SocialStudio.ApiService.Endpoints.Common;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Features.Social.Publications.Connections;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace SocialStudio.ApiService.Endpoints.Social;

/// <summary>
/// Client publish-account connection management (design §9, §15). Org-scoped via
/// <see cref="ICurrentTenant.TenantId"/>. Mutating routes (create/rotate/test) are restricted to org
/// admins — connection management is a credential-management surface, consistent with how
/// <c>OrganizationIntegration</c> management is scoped.
/// </summary>
public static class PublicationConnectionEndpointsExtensions
{
    public static IEndpointRouteBuilder MapPublicationConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social")
            .WithTags("Social Publications Connections")
            .RequireAuthorization();

        group.MapGet("/clients/{clientId:guid}/account-connections", ListAsync)
            .WithName("ListClientAccountConnections")
            .WithSummary("List the client's publish account connections (token-free)")
            .Produces<IReadOnlyList<ClientAccountConnectionResponse>>(StatusCodes.Status200OK);

        group.MapPost("/clients/{clientId:guid}/account-connections", CreateAsync)
            .WithName("CreateClientAccountConnection")
            .WithSummary("Create a client publish account connection (org-admin only)")
            .Produces<ClientAccountConnectionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/account-connections/{connectionId:guid}/token", RotateAsync)
            .WithName("RotateClientAccountConnectionToken")
            .WithSummary("Rotate the access token on a client publish account connection (org-admin only)")
            .Produces<ClientAccountConnectionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/account-connections/{connectionId:guid}/test", TestAsync)
            .WithName("TestClientAccountConnection")
            .WithSummary("Test a client publish account connection against the two-level credential resolution (org-admin only)")
            .Produces<ClientAccountConnectionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid clientId, ICurrentTenant tenant, ISender sender, CancellationToken ct)
    {
        if (tenant.TenantId is not { } org)
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new ListClientAccountConnectionsQuery(org.Value, clientId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> CreateAsync(
        Guid clientId,
        [FromBody] CreateClientAccountConnectionRequest body,
        ICurrentTenant tenant,
        ICurrentUser currentUser,
        ITenantMembershipService membership,
        ISender sender,
        CancellationToken ct)
    {
        if (tenant.TenantId is not { } org || currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        if (!await ClientAccountConnectionAuthorization.IsOrgAdminAsync(membership, org, userId, ct))
        {
            return TypedResults.Forbid();
        }

        var result = await sender.Send(
            new CreateClientAccountConnectionCommand(
                org.Value,
                clientId,
                body.SocialNetworkId,
                body.OrganizationIntegrationId,
                body.ExternalAccountId,
                body.AccessToken,
                body.TokenExpiresAtUtc),
            ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> RotateAsync(
        Guid connectionId,
        [FromBody] RotateClientAccountTokenRequest body,
        ICurrentTenant tenant,
        ICurrentUser currentUser,
        ITenantMembershipService membership,
        ISender sender,
        CancellationToken ct)
    {
        if (tenant.TenantId is not { } org || currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        if (!await ClientAccountConnectionAuthorization.IsOrgAdminAsync(membership, org, userId, ct))
        {
            return TypedResults.Forbid();
        }

        var result = await sender.Send(
            new RotateClientAccountTokenCommand(org.Value, connectionId, body.AccessToken, body.TokenExpiresAtUtc),
            ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> TestAsync(
        Guid connectionId,
        ICurrentTenant tenant,
        ICurrentUser currentUser,
        ITenantMembershipService membership,
        ISender sender,
        CancellationToken ct)
    {
        if (tenant.TenantId is not { } org || currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        if (!await ClientAccountConnectionAuthorization.IsOrgAdminAsync(membership, org, userId, ct))
        {
            return TypedResults.Forbid();
        }

        var result = await sender.Send(new TestClientAccountConnectionCommand(org.Value, connectionId), ct);
        return result.Match(r => (IResult)TypedResults.Ok(r));
    }
}

public sealed record CreateClientAccountConnectionRequest(
    Guid SocialNetworkId,
    Guid OrganizationIntegrationId,
    string ExternalAccountId,
    string AccessToken,
    DateTime? TokenExpiresAtUtc);

public sealed record RotateClientAccountTokenRequest(string AccessToken, DateTime? TokenExpiresAtUtc);
