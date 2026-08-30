using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ContentPlans;
using Incoders.Hive.Application.Features.Social.ContentPlans.GetByPeriod;
using Incoders.Hive.Application.Features.Social.ContentPlans.ListByClient;
using Incoders.Hive.Application.Features.Social.ContentPlans.Upsert;
using Incoders.Hive.Domain.Social.ContentPlans;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class ContentPlanEndpointsExtensions
{
    public static IEndpointRouteBuilder MapContentPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients/{clientId:guid}/content-plans")
            .WithTags("Social ContentPlans")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListContentPlans")
            .WithSummary("List all content plans for a client, ordered Year desc, Month desc")
            .Produces<IReadOnlyList<ContentPlanSummaryResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{year:int}/{month:int}", GetAsync)
            .WithName("GetContentPlan")
            .WithSummary("Get the content plan for a specific year/month period")
            .Produces<ContentPlanResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{year:int}/{month:int}", UpsertAsync)
            .WithName("UpsertContentPlan")
            .WithSummary("Create or update the content plan for a specific year/month period (PUT idempotent)")
            .Produces<ContentPlanResponse>(StatusCodes.Status200OK)
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
            return ContentPlanErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ListContentPlansQuery(clientId, organizationId.Value),
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
            return ContentPlanErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetContentPlanQuery(clientId, organizationId.Value, year, month),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpsertAsync(
        Guid clientId,
        int year,
        int month,
        [FromBody] UpsertContentPlanRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ContentPlanErrors.NotFound.ToProblem();
        }

        var slots = body.Slots?.Select(s => new CalendarSlotRequest(s.Day, s.ContentFormat)).ToList();

        var result = await sender.Send(
            new UpsertContentPlanCommand(
                clientId,
                organizationId.Value,
                year,
                month,
                body.Objectives,
                body.Kpis,
                body.ContentStrategyText,
                slots),
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

public sealed record CalendarSlotRequestBody(
    int Day,
    string ContentFormat);

public sealed record UpsertContentPlanRequest(
    string Objectives,
    IReadOnlyList<string>? Kpis,
    string? ContentStrategyText,
    IReadOnlyList<CalendarSlotRequestBody>? Slots);
