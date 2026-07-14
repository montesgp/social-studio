using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ClientAestheticProfileAdmin.GetProfile;
using Incoders.Hive.Application.Features.Social.ClientAestheticProfileAdmin.UpdateBackgroundPattern;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.Social;

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
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return Domain.Social.Clients.ClientErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetClientAestheticProfileQuery(organizationId.Value, clientId),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpdateBackgroundPatternAsync(
        Guid clientId,
        [FromBody] UpdateClientAestheticProfileBackgroundPatternRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return Domain.Social.Clients.ClientErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new UpdateClientAestheticProfileBackgroundPatternCommand(
                organizationId.Value,
                clientId,
                body.BackgroundPattern),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }
}

public sealed record UpdateClientAestheticProfileBackgroundPatternRequest(
    IReadOnlyList<string> BackgroundPattern);
