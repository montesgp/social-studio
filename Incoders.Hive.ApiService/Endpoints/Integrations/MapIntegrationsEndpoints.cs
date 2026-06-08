using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Integrations;
using Incoders.Hive.Application.Features.Integrations.Configure;
using Incoders.Hive.Application.Features.Integrations.Delete;
using Incoders.Hive.Application.Features.Integrations.GetAllProviders;
using Incoders.Hive.Application.Features.Integrations.GetByProvider;
using Incoders.Hive.Application.Features.Integrations.GetOverview;
using Incoders.Hive.Application.Features.Integrations.SetDefaultAi;
using Incoders.Hive.Application.Features.Integrations.TestConnection;
using Incoders.Hive.Domain.Integrations;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Incoders.Hive.ApiService.Endpoints.Integrations;

public static class IntegrationsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapIntegrationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integrations")
            .WithTags("Integrations")
            .RequireAuthorization();

        group.MapGet("/providers", ListProvidersAsync)
            .WithName("ListIntegrationProviders")
            .WithSummary("List the global integrations provider catalog")
            .Produces<IReadOnlyList<IntegrationProviderResponse>>(StatusCodes.Status200OK);

        group.MapGet("/organization", ListOrganizationIntegrationsAsync)
            .WithName("ListOrganizationIntegrations")
            .WithSummary("List integrations configured for the resolved organization")
            .Produces<OrganizationIntegrationsOverviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/organization/{providerKey}", GetOrganizationIntegrationByProviderAsync)
            .WithName("GetOrganizationIntegrationByProvider")
            .WithSummary("Get one organization integration by provider key for the resolved organization")
            .Produces<OrganizationIntegrationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/organization/{providerKey}", ConfigureOrganizationIntegrationAsync)
            .WithName("ConfigureOrganizationIntegration")
            .WithSummary("Create/update organization integration credentials and mode")
            .Produces<OrganizationIntegrationResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/organization/{providerKey}", DeleteOrganizationIntegrationAsync)
            .WithName("DeleteOrganizationIntegration")
            .WithSummary("Delete organization-owned integration configuration")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/organization/{providerKey}/test", TestConnectionAsync)
            .WithName("TestOrganizationIntegrationConnection")
            .WithSummary("Test integration connectivity for the resolved organization")
            .Produces<OrganizationIntegrationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/organization/default-ai", SetDefaultAiAsync)
            .WithName("SetDefaultAiIntegration")
            .WithSummary("Select default AI provider/model for the resolved organization")
            .Produces<OrganizationIntegrationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // ─── Canva per-user OAuth (PKCE) — WU-5 ─────────────────────────────────

        var canvaGroup = app.MapGroup("/api/integrations/canva")
            .WithTags("Integrations")
            .RequireAuthorization();

        // Authorize requires auth (called by the admin's browser via fetch with the bearer): it
        // returns the Canva authorize URL as JSON so the SPA can navigate to Canva.
        canvaGroup.MapGet("/authorize", CanvaOAuthFacade.AuthorizeAsync)
            .WithName("CanvaOAuthAuthorize")
            .WithSummary("Start Canva org OAuth PKCE flow — returns the Canva authorize URL as JSON")
            .Produces<CanvaAuthorizeUrlResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Callback is an UNAUTHENTICATED browser navigation FROM Canva (no bearer) — it must allow
        // anonymous; security comes from validating the OAuth state against the server-side cache.
        canvaGroup.MapGet("/callback", CanvaOAuthFacade.CallbackAsync)
            .AllowAnonymous()
            .WithName("CanvaOAuthCallback")
            .WithSummary("Canva OAuth callback — exchanges code for the org token and redirects to UI")
            .Produces(StatusCodes.Status302Found);

        return app;
    }

    private static async Task<IResult> ListProvidersAsync(ISender sender, CancellationToken cancellationToken)
    {
        var providers = await sender.Send(new GetIntegrationProvidersQuery(), cancellationToken);
        return TypedResults.Ok(providers);
    }

    private static async Task<IResult> ListOrganizationIntegrationsAsync(
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return IntegrationErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new GetOrganizationIntegrationsQuery(organizationId.Value),
            cancellationToken);

        return result.Match(overview => (IResult)TypedResults.Ok(overview));
    }

    private static async Task<IResult> ConfigureOrganizationIntegrationAsync(
        string providerKey,
        [FromBody] ConfigureOrganizationIntegrationRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return IntegrationErrors.OrganizationRequired.ToProblem();
        }

        var credentialMap = (body.Credentials ?? Array.Empty<IntegrationCredentialInput>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var result = await sender.Send(
            new ConfigureOrganizationIntegrationCommand(
                organizationId.Value,
                providerKey,
                body.ConnectionMode,
                body.Enabled,
                body.SelectedAiModel,
                credentialMap),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    private static async Task<IResult> GetOrganizationIntegrationByProviderAsync(
        string providerKey,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return IntegrationErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new GetOrganizationIntegrationByProviderQuery(organizationId.Value, providerKey),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    private static async Task<IResult> DeleteOrganizationIntegrationAsync(
        string providerKey,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return IntegrationErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new DeleteOrganizationIntegrationCommand(organizationId.Value, providerKey),
            cancellationToken);

        return result.Match(() => TypedResults.NoContent());
    }

    private static async Task<IResult> TestConnectionAsync(
        string providerKey,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return IntegrationErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new TestOrganizationIntegrationConnectionCommand(organizationId.Value, providerKey),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    private static async Task<IResult> SetDefaultAiAsync(
        [FromBody] SetDefaultAiIntegrationRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return IntegrationErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new SetDefaultAiIntegrationCommand(organizationId.Value, body.ProviderKey, body.Model),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

}

/// <summary>
/// Minimal API handler delegates for Canva per-user OAuth endpoints (WU-5).
///
/// <para>
/// Authorize: builds PKCE URL and redirects the user to Canva.
/// Callback: validates state, exchanges code+verifier for tokens, redirects to the UI.
/// </para>
///
/// <para>
/// Errors (invalid/expired state, Canva error) produce a redirect to the error page —
/// no HTTP 500 is ever returned for expected OAuth failures.
/// </para>
/// </summary>
public static class CanvaOAuthFacade
{
    // UI success and error redirect paths — the SPA handles these routes.
    private const string SuccessRedirectPath = "/integrations/canva/connected";
    private const string ErrorRedirectPath = "/integrations/canva/error";

    /// <summary>
    /// GET /api/integrations/canva/authorize?orgIntegrationId={guid}&amp;redirectUri={uri}
    ///
    /// Resolves the user + org context, calls <see cref="ICanvaOAuthService.BuildAuthorizeUrlAsync"/>,
    /// stores verifier in cache, then 302-redirects to Canva.
    /// </summary>
    public static async Task<IResult> AuthorizeAsync(
        [FromQuery] Guid orgIntegrationId,
        [FromQuery] string redirectUri,
        [FromServices] ICanvaOAuthService oauthService,
        [FromServices] ICurrentUser currentUser,
        [FromServices] ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
            return TypedResults.Unauthorized();

        if (currentTenant.TenantId is not { } tenantId)
            return IntegrationErrors.OrganizationRequired.ToProblem();

        // Org-level connection: the token is stored under the shared org sentinel, not the
        // initiating user — so the whole org reuses this single connection.
        var result = await oauthService.BuildAuthorizeUrlAsync(
            CanvaOrgConnection.SharedUserId,
            orgIntegrationId,
            tenantId.Value,
            redirectUri,
            cancellationToken);

        // Return the URL as JSON — the SPA fetches this (with its bearer) then navigates the
        // browser to Canva. A 302 here would be useless: the SPA can't send the bearer on a
        // full-page navigation.
        return result.IsSuccess
            ? TypedResults.Ok(new CanvaAuthorizeUrlResponse(result.Value))
            : result.Error.ToProblem();
    }

    /// <summary>
    /// GET /api/integrations/canva/callback?code={code}&amp;state={state}&amp;orgIntegrationId={guid}&amp;redirectUri={uri}
    ///
    /// Validates state, exchanges code+verifier for tokens, redirects to the UI success or error page.
    /// Expected failures (invalid/expired state, Canva error) redirect to the error page — no 500.
    /// </summary>
    public static async Task<IResult> CallbackAsync(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromServices] ICanvaOAuthService oauthService,
        CancellationToken cancellationToken)
    {
        // Canva may return an error param instead of a code (e.g., the user cancelled).
        if (!string.IsNullOrWhiteSpace(error))
            return TypedResults.Redirect(ErrorRedirectPath + "?reason=" + Uri.EscapeDataString(error));

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return TypedResults.Redirect(ErrorRedirectPath + "?reason=no-code");

        // No bearer/tenant here (browser navigation from Canva): the org + integration + verifier
        // + redirect URI are all recovered from the server-side cache keyed by the validated state.
        var result = await oauthService.ExchangeCodeAsync(code, state, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Redirect(SuccessRedirectPath)
            : TypedResults.Redirect(ErrorRedirectPath + "?reason=" + Uri.EscapeDataString(result.Error.Code));
    }
}

/// <summary>Response of GET .../canva/authorize: the Canva authorization URL the SPA navigates to.</summary>
public sealed record CanvaAuthorizeUrlResponse(string AuthorizeUrl);

public sealed record ConfigureOrganizationIntegrationRequest(
    IntegrationConnectionMode ConnectionMode,
    bool Enabled,
    string? SelectedAiModel,
    IReadOnlyList<IntegrationCredentialInput>? Credentials);

public sealed record IntegrationCredentialInput(string Key, string? Value);

public sealed record SetDefaultAiIntegrationRequest(string ProviderKey, string? Model);
