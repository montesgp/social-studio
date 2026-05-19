using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals.Activate;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals.Create;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals.Deactivate;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals.Dtos;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals.GetActive;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals.GetById;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals.List;
using Incoders.Hive.Application.Features.Social.ClientBrandManuals.Update;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientBrandManuals;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class ClientBrandManualsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapClientBrandManualsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients/{clientId:guid}/brand-manuals")
            .WithTags("Client Brand Manuals")
            .RequireAuthorization();

        group.MapPost("/", CreateAsync)
            .WithName("CreateClientBrandManual")
            .WithSummary("Create a brand manual for a client (auto-deactivates prior active)")
            .Produces<ClientBrandManualResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", ListAsync)
            .WithName("ListClientBrandManuals")
            .WithSummary("List brand manuals for a client, paginated")
            .Produces<PagedClientBrandManualsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // IMPORTANT: /active MUST be registered BEFORE /{id:guid} — Resolution 2 / ADR Route Ordering
        group.MapGet("/active", GetActiveAsync)
            .WithName("GetActiveClientBrandManual")
            .WithSummary("Get the currently active brand manual for a client")
            .Produces<ClientBrandManualResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetClientBrandManualById")
            .WithSummary("Get a brand manual by id")
            .Produces<ClientBrandManualResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateClientBrandManual")
            .WithSummary("Update a brand manual (bulk-edit structured fields)")
            .Produces<ClientBrandManualResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("DeactivateClientBrandManual")
            .WithSummary("Deactivate a brand manual")
            .Produces<ClientBrandManualResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("ActivateClientBrandManual")
            .WithSummary("Reactivate a brand manual")
            .Produces<ClientBrandManualResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        Guid clientId,
        [FromBody] CreateClientBrandManualRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientBrandManualErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new CreateClientBrandManualCommand(
                organizationId.Value,
                clientId,
                body.Title,
                body.ColorPalette,
                body.Logos,
                body.BackgroundTemplates,
                body.Typography,
                body.VoiceAndTone,
                body.Notes),
            cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/social/clients/{clientId}/brand-manuals/{result.Value.Id}", result.Value)
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
            return ClientBrandManualErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ListClientBrandManualsByClientQuery(
                organizationId.Value, clientId, includeInactive, page, pageSize),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> GetActiveAsync(
        Guid clientId,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientBrandManualErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetActiveClientBrandManualQuery(organizationId.Value, clientId),
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
            return ClientBrandManualErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new GetClientBrandManualByIdQuery(organizationId.Value, clientId, id),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    private static async Task<IResult> UpdateAsync(
        Guid clientId,
        Guid id,
        [FromBody] UpdateClientBrandManualRequest body,
        ICurrentTenant currentTenant,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientBrandManualErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new UpdateClientBrandManualCommand(
                organizationId.Value,
                clientId,
                id,
                body.Title,
                body.ColorPalette,
                body.Logos,
                body.BackgroundTemplates,
                body.Typography,
                body.VoiceAndTone,
                body.Notes),
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
            return ClientBrandManualErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new DeactivateClientBrandManualCommand(organizationId.Value, clientId, id),
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
            return ClientBrandManualErrors.NotFound.ToProblem();
        }

        var result = await sender.Send(
            new ActivateClientBrandManualCommand(organizationId.Value, clientId, id),
            cancellationToken);

        return result.Match(r => (IResult)TypedResults.Ok(r));
    }

    /// <summary>
    /// Maps domain errors to HTTP status codes.
    /// EmptyManual and Validation errors → 422 Unprocessable Entity (R-API-005).
    /// AlreadyActive / AlreadyInactive / AnotherManualActive → 409 Conflict.
    /// NotFound → 404.
    /// </summary>
    private static IResult MapError(Error error)
    {
        if (error.Type == ErrorType.Validation)
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

public sealed record CreateClientBrandManualRequest(
    string Title,
    IReadOnlyList<ColorEntryDto> ColorPalette,
    IReadOnlyList<LogoEntryDto> Logos,
    IReadOnlyList<BackgroundEntryDto> BackgroundTemplates,
    IReadOnlyList<TypographyEntryDto> Typography,
    string? VoiceAndTone,
    string? Notes);

public sealed record UpdateClientBrandManualRequest(
    string Title,
    IReadOnlyList<ColorEntryDto> ColorPalette,
    IReadOnlyList<LogoEntryDto> Logos,
    IReadOnlyList<BackgroundEntryDto> BackgroundTemplates,
    IReadOnlyList<TypographyEntryDto> Typography,
    string? VoiceAndTone,
    string? Notes);
