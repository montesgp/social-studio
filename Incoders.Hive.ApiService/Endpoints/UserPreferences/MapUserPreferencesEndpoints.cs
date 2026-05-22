using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Features.UserPreferences;
using Incoders.Hive.Application.Features.UserPreferences.GetCurrent;
using Incoders.Hive.Application.Features.UserPreferences.Update;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.UserPreferences;

public static class UserPreferencesEndpointsExtensions
{
    public static IEndpointRouteBuilder MapUserPreferencesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/preferences")
            .WithTags("User Preferences")
            .RequireAuthorization();

        group.MapGet(string.Empty, GetCurrentAsync)
            .WithName("GetCurrentUserPreferences")
            .WithSummary("Get current authenticated user preferences")
            .Produces<UserPreferenceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut(string.Empty, UpdateAsync)
            .WithName("UpdateCurrentUserPreferences")
            .WithSummary("Update current authenticated user preferences")
            .Produces<UserPreferenceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> GetCurrentAsync(
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCurrentUserPreferenceQuery(), cancellationToken);
        return result.Match(preferences => (IResult)TypedResults.Ok(preferences));
    }

    private static async Task<IResult> UpdateAsync(
        [FromBody] UpdateCurrentUserPreferenceRequest body,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateCurrentUserPreferenceCommand(body.ThemeKey, body.AccentTheme),
            cancellationToken);

        return result.Match(preferences => (IResult)TypedResults.Ok(preferences));
    }
}

public sealed record UpdateCurrentUserPreferenceRequest(string ThemeKey, string? AccentTheme);
