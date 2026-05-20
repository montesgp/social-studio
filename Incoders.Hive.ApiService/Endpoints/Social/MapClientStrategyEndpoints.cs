using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ClientStrategies.GetByClientId;
using Incoders.Hive.Application.Features.Social.ClientStrategies.Upsert;
using Incoders.Hive.Domain.Social.ClientStrategies;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class ClientStrategyEndpointsExtensions
{
    public static IEndpointRouteBuilder MapClientStrategyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients/{clientId:guid}/strategy")
            .WithTags("Social ClientStrategy")
            .RequireAuthorization();

        group.MapGet("/", GetAsync)
            .WithName("GetClientStrategy")
            .WithSummary("Get the social media strategy for a client")
            .Produces<ClientStrategyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/", UpsertAsync)
            .WithName("UpsertClientStrategy")
            .WithSummary("Create or update the social media strategy for a client (PUT idempotent)")
            .Produces<ClientStrategyResponse>(StatusCodes.Status200OK)
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
            return ClientStrategyErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetClientStrategyQuery(clientId, organizationId.Value),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpsertAsync(
        Guid clientId,
        [FromBody] UpsertClientStrategyRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientStrategyErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new UpsertClientStrategyCommand(
                clientId,
                organizationId.Value,
                body.ContentPillars,
                body.ToneKeywords,
                body.TargetAudience,
                body.PostingFrequency,
                body.Notes),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : MapError(result.Error);
    }

    private static IResult MapError(Domain.Common.Error error)
    {
        if (error.Type == Domain.Common.ErrorType.Validation)
        {
            return TypedResults.Problem(
                title: "Unprocessable Entity",
                detail: error.MessageKey,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });
        }

        return error.ToProblem();
    }
}

public sealed record UpsertClientStrategyRequest(
    IReadOnlyList<string>? ContentPillars,
    IReadOnlyList<string>? ToneKeywords,
    string? TargetAudience,
    string? PostingFrequency,
    string? Notes);
