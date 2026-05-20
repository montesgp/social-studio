using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Activate;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Create;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Deactivate;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.GetById;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.GetByClient;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Reorder;
using Incoders.Hive.Application.Features.Social.ClientReferentAccounts.Update;
using Incoders.Hive.Domain.Social.ClientReferentAccounts;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class ClientReferentAccountsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapClientReferentAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients/{clientId:guid}/referent-accounts")
            .WithTags("Client Referent Accounts")
            .RequireAuthorization();

        group.MapPost("/", CreateAsync)
            .WithName("CreateClientReferentAccount")
            .WithSummary("Create a referent account for a client on a social network")
            .Produces<ClientReferentAccountResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", ListAsync)
            .WithName("ListClientReferentAccounts")
            .WithSummary("List referent accounts for a client, paginated")
            .Produces<PagedClientReferentAccountsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetClientReferentAccountById")
            .WithSummary("Get a referent account by id")
            .Produces<ClientReferentAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateClientReferentAccount")
            .WithSummary("Update a referent account")
            .Produces<ClientReferentAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivateClientReferentAccount")
            .WithSummary("Deactivate (soft-archive) a referent account")
            .Produces<ClientReferentAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("ActivateClientReferentAccount")
            .WithSummary("Reactivate a referent account")
            .Produces<ClientReferentAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // IMPORTANT: /reorder is a static segment — registered BEFORE /{id:guid} variants to avoid ambiguity
        group.MapPost("/reorder", ReorderAsync)
            .WithName("ReorderClientReferentAccounts")
            .WithSummary("Reorder referent accounts by supplying the full ordered list of IDs (0-based position)")
            .Produces<IReadOnlyList<ReorderedClientReferentAccountResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        Guid clientId,
        [FromBody] CreateClientReferentAccountRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientReferentAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new CreateClientReferentAccountCommand(
                organizationId.Value,
                clientId,
                body.SocialNetworkId,
                body.ProfileUrl,
                body.Handle,
                body.Label,
                body.Notes),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/social/clients/{clientId}/referent-accounts/{result.Value.Id}", result.Value)
            : MapError(result.Error);
    }

    private static async Task<IResult> ListAsync(
        Guid clientId,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientReferentAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ListClientReferentAccountsByClientQuery(
                organizationId.Value,
                clientId,
                includeInactive,
                page,
                pageSize),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid clientId,
        Guid id,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientReferentAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetClientReferentAccountByIdQuery(organizationId.Value, clientId, id),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpdateAsync(
        Guid clientId,
        Guid id,
        [FromBody] UpdateClientReferentAccountRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientReferentAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new UpdateClientReferentAccountCommand(
                organizationId.Value,
                clientId,
                id,
                body.ProfileUrl,
                body.Handle,
                body.Label,
                body.Notes),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> DeactivateAsync(
        Guid clientId,
        Guid id,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientReferentAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new DeactivateClientReferentAccountCommand(organizationId.Value, clientId, id),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> ActivateAsync(
        Guid clientId,
        Guid id,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientReferentAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ActivateClientReferentAccountCommand(organizationId.Value, clientId, id),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> ReorderAsync(
        Guid clientId,
        [FromBody] ReorderClientReferentAccountsRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientReferentAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ReorderClientReferentAccountsCommand(
                clientId,
                organizationId.Value,
                body.OrderedIds),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : MapError(result.Error);
    }

    /// <summary>
    /// Maps domain errors to HTTP status codes, including 422 for SocialNetworkInactive.
    /// </summary>
    private static IResult MapError(Domain.Common.Error error)
    {
        // SocialNetworkInactive is Validation type but maps to 422 (per R-API-005)
        if (error.Code == ClientReferentAccountErrors.SocialNetworkInactive.Code)
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

public sealed record CreateClientReferentAccountRequest(
    Guid SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes);

public sealed record UpdateClientReferentAccountRequest(
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes);

public sealed record ReorderClientReferentAccountsRequest(IReadOnlyList<Guid> OrderedIds);
