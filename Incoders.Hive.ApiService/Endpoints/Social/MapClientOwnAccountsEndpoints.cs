using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts.Activate;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts.Create;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts.Deactivate;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts.GetById;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts.GetByClient;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts.Reorder;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts.Sync;
using Incoders.Hive.Application.Features.Social.ClientOwnAccounts.Update;
using Incoders.Hive.Domain.Social.ClientOwnAccounts;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class ClientOwnAccountsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapClientOwnAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients/{clientId:guid}/own-accounts")
            .WithTags("Client Own Accounts")
            .RequireAuthorization();

        group.MapPost("/", CreateAsync)
            .WithName("CreateClientOwnAccount")
            .WithSummary("Create an own account for a client on a social network")
            .Produces<ClientOwnAccountResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", ListAsync)
            .WithName("ListClientOwnAccounts")
            .WithSummary("List own accounts for a client, paginated")
            .Produces<PagedClientOwnAccountsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/", SyncAsync)
            .WithName("SyncClientOwnAccounts")
            .WithSummary("Replace the client's own account draft with the supplied ordered list")
            .Produces<IReadOnlyList<ClientOwnAccountResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetClientOwnAccountById")
            .WithSummary("Get an own account by id")
            .Produces<ClientOwnAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateClientOwnAccount")
            .WithSummary("Update an own account")
            .Produces<ClientOwnAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivateClientOwnAccount")
            .WithSummary("Deactivate (soft-archive) an own account")
            .Produces<ClientOwnAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("ActivateClientOwnAccount")
            .WithSummary("Reactivate an own account")
            .Produces<ClientOwnAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // IMPORTANT: /reorder is a static segment — registered BEFORE /{id:guid} variants to avoid ambiguity
        group.MapPost("/reorder", ReorderAsync)
            .WithName("ReorderClientOwnAccounts")
            .WithSummary("Reorder own accounts by supplying the full ordered list of IDs (0-based position)")
            .Produces<IReadOnlyList<ReorderedClientOwnAccountResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        Guid clientId,
        [FromBody] CreateClientOwnAccountRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientOwnAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new CreateClientOwnAccountCommand(
                organizationId.Value,
                clientId,
                body.SocialNetworkId,
                body.ProfileUrl,
                body.Handle,
                body.Label,
                body.Notes),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/social/clients/{clientId}/own-accounts/{result.Value.Id}", result.Value)
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
            return ClientOwnAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ListClientOwnAccountsByClientQuery(
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
            return ClientOwnAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetClientOwnAccountByIdQuery(organizationId.Value, clientId, id),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpdateAsync(
        Guid clientId,
        Guid id,
        [FromBody] UpdateClientOwnAccountRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientOwnAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new UpdateClientOwnAccountCommand(
                organizationId.Value,
                clientId,
                id,
                body.SocialNetworkId,
                body.ProfileUrl,
                body.Handle,
                body.Label,
                body.Notes),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> SyncAsync(
        Guid clientId,
        [FromBody] SyncClientOwnAccountsRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientOwnAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new SyncClientOwnAccountsCommand(
                organizationId.Value,
                clientId,
                body.Items.Select(x => new SyncClientOwnAccountItem(
                    x.Id,
                    x.SocialNetworkId,
                    x.ProfileUrl,
                    x.Handle,
                    x.Label,
                    x.Notes,
                    x.IsActive)).ToList()),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : MapError(result.Error);
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
            return ClientOwnAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new DeactivateClientOwnAccountCommand(organizationId.Value, clientId, id),
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
            return ClientOwnAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ActivateClientOwnAccountCommand(organizationId.Value, clientId, id),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> ReorderAsync(
        Guid clientId,
        [FromBody] ReorderClientOwnAccountsRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientOwnAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ReorderClientOwnAccountsCommand(
                clientId,
                organizationId.Value,
                body.OrderedIds),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : MapError(result.Error);
    }

    private static IResult MapError(Domain.Common.Error error)
    {
        if (error.Code == ClientOwnAccountErrors.SocialNetworkInactive.Code)
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

public sealed record CreateClientOwnAccountRequest(
    Guid SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes);

public sealed record UpdateClientOwnAccountRequest(
    Guid? SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes);

public sealed record ReorderClientOwnAccountsRequest(IReadOnlyList<Guid> OrderedIds);

public sealed record SyncClientOwnAccountsRequest(
    IReadOnlyList<SyncClientOwnAccountRequestItem> Items);

public sealed record SyncClientOwnAccountRequestItem(
    Guid? Id,
    Guid SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes,
    bool IsActive);
