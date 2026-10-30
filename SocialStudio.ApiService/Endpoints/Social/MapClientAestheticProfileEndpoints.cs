using SocialStudio.ApiService.Endpoints.Common;
using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Features.Social.ClientAestheticProfileAdmin.GetProfile;
using SocialStudio.Application.Features.Social.ClientAestheticProfileAdmin.UpdateBackgroundPattern;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace SocialStudio.ApiService.Endpoints.Social;

/// <summary>
/// client-analysis-data admin panel: exposes the client's captured aesthetic profile
/// (read-only, full detail) and lets an admin correct the BackgroundPattern by hand — the
/// unblock for clients whose extraction produced sparse/wrong per-slide photo-vs-flat data.
/// Mirrors <see cref="ClientOwnAccountsEndpointsExtensions"/> conventions.
/// </summary>
public static class ClientAestheticProfileEndpointsExtensions
{
    public static IEndpointRouteBuilder MapClientAestheticProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients/{clientId:guid}/aesthetic-profile")
            .WithTags("Client Aesthetic Profile")
            .RequireAuthorization();

        group.MapGet("/", GetAsync)
            .WithName("GetClientAestheticProfile")
            .WithSummary("Get the client's latest captured aesthetic profile analysis (full detail)")
            .Produces<ClientAestheticProfileAdminResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/background-pattern", UpdateBackgroundPatternAsync)
            .WithName("UpdateClientAestheticProfileBackgroundPattern")
            .WithSummary("Patch the per-slide background pattern (Photo/FlatColor/Decorator) on the client's latest analyzed profile")
            .Produces<UpdateClientAestheticProfileBackgroundPatternResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> GetAsync(
        Guid clientId,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!TryResolveOrganization(currentTenant, currentUser, out var organizationId))
        {
            return Domain.Social.Clients.ClientErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetClientAestheticProfileQuery(organizationId, clientId),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpdateBackgroundPatternAsync(
        Guid clientId,
        [FromBody] UpdateClientAestheticProfileBackgroundPatternRequest body,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!TryResolveOrganization(currentTenant, currentUser, out var organizationId))
        {
            return Domain.Social.Clients.ClientErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new UpdateClientAestheticProfileBackgroundPatternCommand(
                organizationId,
                clientId,
                body.BackgroundPattern),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    /// <summary>
    /// Mirrors the by-id client endpoint: a platform admin resolves the client across every
    /// organization (<c>null</c>), since the admin clients list spans organizations they are not a
    /// member of. Everyone else stays scoped to their resolved tenant.
    /// </summary>
    private static bool TryResolveOrganization(
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        out Guid? organizationId)
    {
        if (currentUser.IsPlatformAdmin)
        {
            organizationId = null;
            return true;
        }

        if (currentTenant.TenantId is { } tenantId)
        {
            organizationId = tenantId.Value;
            return true;
        }

        organizationId = null;
        return false;
    }
}

public sealed record UpdateClientAestheticProfileBackgroundPatternRequest(
    IReadOnlyList<string> BackgroundPattern);
