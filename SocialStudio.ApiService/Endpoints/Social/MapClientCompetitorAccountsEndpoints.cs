using SocialStudio.ApiService.Endpoints.Common;
using SocialStudio.Application.Abstractions.Tenancy;
using SocialStudio.Application.Features.Social.ClientCompetitorAccounts;
using SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Activate;
using SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Create;
using SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Deactivate;
using SocialStudio.Application.Features.Social.ClientCompetitorAccounts.GetById;
using SocialStudio.Application.Features.Social.ClientCompetitorAccounts.GetByClient;
using SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Reorder;
using SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Sync;
using SocialStudio.Application.Features.Social.ClientCompetitorAccounts.Update;
using SocialStudio.Domain.Social.ClientCompetitorAccounts;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace SocialStudio.ApiService.Endpoints.Social;

public static class ClientCompetitorAccountsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapClientCompetitorAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients/{clientId:guid}/competitor-accounts")
            .WithTags("Client Competitor Accounts")
            .RequireAuthorization();

        group.MapPost("/", CreateAsync)
            .WithName("CreateClientCompetitorAccount")
            .WithSummary("Create a competitor account for a client on a social network")
            .Produces<ClientCompetitorAccountResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", ListAsync)
            .WithName("ListClientCompetitorAccounts")
            .WithSummary("List competitor accounts for a client, paginated")
            .Produces<PagedClientCompetitorAccountsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/", SyncAsync)
            .WithName("SyncClientCompetitorAccounts")
            .WithSummary("Replace the client's competitor account draft with the supplied ordered list")
            .Produces<IReadOnlyList<ClientCompetitorAccountResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetClientCompetitorAccountById")
            .WithSummary("Get a competitor account by id")
            .Produces<ClientCompetitorAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateClientCompetitorAccount")
            .WithSummary("Update a competitor account")
            .Produces<ClientCompetitorAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivateClientCompetitorAccount")
            .WithSummary("Deactivate (soft-archive) a competitor account")
            .Produces<ClientCompetitorAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("ActivateClientCompetitorAccount")
            .WithSummary("Reactivate a competitor account")
            .Produces<ClientCompetitorAccountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // IMPORTANT: /reorder is a static segment — registered BEFORE /{id:guid} variants to avoid ambiguity
        group.MapPost("/reorder", ReorderAsync)
            .WithName("ReorderClientCompetitorAccounts")
            .WithSummary("Reorder competitor accounts by supplying the full ordered list of IDs (0-based position)")
            .Produces<IReadOnlyList<ReorderedClientCompetitorAccountResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        Guid clientId,
        [FromBody] CreateClientCompetitorAccountRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientCompetitorAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new CreateClientCompetitorAccountCommand(
                organizationId.Value,
                clientId,
                body.SocialNetworkId,
                body.ProfileUrl,
                body.Handle,
                body.Label,
                body.Notes),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/social/clients/{clientId}/competitor-accounts/{result.Value.Id}", result.Value)
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
            return ClientCompetitorAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ListClientCompetitorAccountsByClientQuery(
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
            return ClientCompetitorAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetClientCompetitorAccountByIdQuery(organizationId.Value, clientId, id),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpdateAsync(
        Guid clientId,
        Guid id,
        [FromBody] UpdateClientCompetitorAccountRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientCompetitorAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new UpdateClientCompetitorAccountCommand(
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
        [FromBody] SyncClientCompetitorAccountsRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientCompetitorAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new SyncClientCompetitorAccountsCommand(
                organizationId.Value,
                clientId,
                body.Items.Select(x => new SyncClientCompetitorAccountItem(
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
            return ClientCompetitorAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new DeactivateClientCompetitorAccountCommand(organizationId.Value, clientId, id),
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
            return ClientCompetitorAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ActivateClientCompetitorAccountCommand(organizationId.Value, clientId, id),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> ReorderAsync(
        Guid clientId,
        [FromBody] ReorderClientCompetitorAccountsRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientCompetitorAccountErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ReorderClientCompetitorAccountsCommand(
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
        if (error.Code == ClientCompetitorAccountErrors.SocialNetworkInactive.Code)
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

public sealed record CreateClientCompetitorAccountRequest(
    Guid SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes);

public sealed record UpdateClientCompetitorAccountRequest(
    Guid? SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes);

public sealed record ReorderClientCompetitorAccountsRequest(IReadOnlyList<Guid> OrderedIds);

public sealed record SyncClientCompetitorAccountsRequest(
    IReadOnlyList<SyncClientCompetitorAccountRequestItem> Items);

public sealed record SyncClientCompetitorAccountRequestItem(
    Guid? Id,
    Guid SocialNetworkId,
    string ProfileUrl,
    string? Handle,
    string? Label,
    string? Notes,
    bool IsActive);
