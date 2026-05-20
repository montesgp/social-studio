using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.MonthlyObjectives.GetByClientAndMonth;
using Incoders.Hive.Application.Features.Social.MonthlyObjectives.ListByClient;
using Incoders.Hive.Application.Features.Social.MonthlyObjectives.Upsert;
using Incoders.Hive.Domain.Social.MonthlyObjectives;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class MonthlyObjectivesEndpointsExtensions
{
    public static IEndpointRouteBuilder MapMonthlyObjectivesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients/{clientId:guid}/objectives")
            .WithTags("Social MonthlyObjectives")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListMonthlyObjectives")
            .WithSummary("List all monthly objectives for a client, ordered Year desc, Month desc")
            .Produces<MonthlyObjectivesListResponse>(StatusCodes.Status200OK);

        group.MapGet("/{year:int}/{month:int}", GetAsync)
            .WithName("GetMonthlyObjective")
            .WithSummary("Get the monthly objective for a specific year/month")
            .Produces<MonthlyObjectiveResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{year:int}/{month:int}", UpsertAsync)
            .WithName("UpsertMonthlyObjective")
            .WithSummary("Create or update the monthly objective for a specific year/month (PUT idempotent)")
            .Produces<MonthlyObjectiveResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid clientId,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return MonthlyObjectiveErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ListMonthlyObjectivesQuery(clientId, organizationId.Value),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetAsync(
        Guid clientId,
        int year,
        int month,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return MonthlyObjectiveErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetMonthlyObjectiveQuery(clientId, organizationId.Value, year, month),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpsertAsync(
        Guid clientId,
        int year,
        int month,
        [FromBody] UpsertMonthlyObjectiveRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return MonthlyObjectiveErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new UpsertMonthlyObjectiveCommand(
                clientId,
                organizationId.Value,
                year,
                month,
                body.Objectives,
                body.Kpis),
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

public sealed record UpsertMonthlyObjectiveRequest(
    string Objectives,
    IReadOnlyList<string>? Kpis);
