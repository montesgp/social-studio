using Incoders.Hive.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.Common;

public static class MapMeEndpointsExtensions
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me")
            .WithTags("Me")
            .RequireAuthorization();

        group.MapGet("/", GetCurrentUser)
            .WithName("GetCurrentUser")
            .WithSummary("Returns the current authenticated user's identity including platform admin status")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static IResult GetCurrentUser([FromServices] ICurrentUser currentUser)
    {
        return Results.Ok(new MeResponse(
            currentUser.UserId,
            currentUser.Email,
            currentUser.IsPlatformAdmin));
    }
}

public sealed record MeResponse(Guid? UserId, string? Email, bool IsPlatformAdmin);
