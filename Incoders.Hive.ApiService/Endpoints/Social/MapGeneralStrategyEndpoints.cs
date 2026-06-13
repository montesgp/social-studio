using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.GeneralStrategies.GetByClientId;
using Incoders.Hive.Application.Features.Social.GeneralStrategies.Upsert;
using Incoders.Hive.Domain.Social.GeneralStrategies;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class GeneralStrategyEndpointsExtensions
{
    public static IEndpointRouteBuilder MapGeneralStrategyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients/{clientId:guid}/general-strategy")
            .WithTags("Social GeneralStrategy")
            .RequireAuthorization();

        group.MapGet("/", GetAsync)
            .WithName("GetGeneralStrategy")
            .WithSummary("Get the general social media strategy for a client")
            .Produces<GeneralStrategyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/", UpsertAsync)
            .WithName("UpsertGeneralStrategy")
            .WithSummary("Create or update the general social media strategy for a client (PUT idempotent)")
            .Produces<GeneralStrategyResponse>(StatusCodes.Status200OK)
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
            return GeneralStrategyErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetGeneralStrategyQuery(clientId, organizationId.Value),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpsertAsync(
        Guid clientId,
        [FromBody] UpsertGeneralStrategyRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return GeneralStrategyErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new UpsertGeneralStrategyCommand(
                clientId,
                organizationId.Value,
                body.ContentPillars,
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

public sealed record UpsertGeneralStrategyRequest(
    IReadOnlyList<string>? ContentPillars,
    string? TargetAudience,
    string? PostingFrequency,
    string? Notes);
