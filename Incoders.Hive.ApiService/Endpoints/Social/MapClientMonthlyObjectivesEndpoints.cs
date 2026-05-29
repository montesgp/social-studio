using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.GetByClientAndMonth;
using Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.ListByClient;
using Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.Upsert;
using Incoders.Hive.Domain.Social.ClientMonthlyObjectives;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class ClientMonthlyObjectivesEndpointsExtensions
{
    public static IEndpointRouteBuilder MapClientMonthlyObjectivesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients/{clientId:guid}/objectives")
            .WithTags("Social ClientMonthlyObjectives")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListClientMonthlyObjectives")
            .WithSummary("List all monthly objectives for a client, ordered Year desc, Month desc")
            .Produces<ClientMonthlyObjectivesListResponse>(StatusCodes.Status200OK);

        group.MapGet("/{year:int}/{month:int}", GetAsync)
            .WithName("GetClientMonthlyObjective")
            .WithSummary("Get the monthly objective for a specific year/month")
            .Produces<ClientMonthlyObjectiveResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{year:int}/{month:int}", UpsertAsync)
            .WithName("UpsertClientMonthlyObjective")
            .WithSummary("Create or update the monthly objective for a specific year/month (PUT idempotent)")
            .Produces<ClientMonthlyObjectiveResponse>(StatusCodes.Status200OK)
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
            return ClientMonthlyObjectiveErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ListClientMonthlyObjectivesQuery(clientId, organizationId.Value),
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
            return ClientMonthlyObjectiveErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetClientMonthlyObjectiveQuery(clientId, organizationId.Value, year, month),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpsertAsync(
        Guid clientId,
        int year,
        int month,
        [FromBody] UpsertClientMonthlyObjectiveRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientMonthlyObjectiveErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new UpsertClientMonthlyObjectiveCommand(
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

public sealed record UpsertClientMonthlyObjectiveRequest(
    string Objectives,
    IReadOnlyList<string>? Kpis);
