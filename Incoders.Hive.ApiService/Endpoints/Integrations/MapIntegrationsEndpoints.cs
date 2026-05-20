using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Integrations;
using Incoders.Hive.Application.Features.Integrations.Configure;
using Incoders.Hive.Application.Features.Integrations.Delete;
using Incoders.Hive.Application.Features.Integrations.GetAllProviders;
using Incoders.Hive.Application.Features.Integrations.GetByProvider;
using Incoders.Hive.Application.Features.Integrations.GetOverview;
using Incoders.Hive.Application.Features.Integrations.RoutingPolicies;
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

        group.MapGet("/organization/routing-policies", ListRoutingPoliciesAsync)
            .WithName("ListOrganizationRoutingPolicies")
            .WithSummary("List organization routing policies for provider orchestration")
            .Produces<IReadOnlyList<OrganizationRoutingPolicyResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/organization/routing-policies", UpsertRoutingPolicyAsync)
            .WithName("UpsertOrganizationRoutingPolicy")
            .WithSummary("Create or update one organization routing policy")
            .Produces<OrganizationRoutingPolicyResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

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

    private static async Task<IResult> ListRoutingPoliciesAsync(
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken,
        [FromQuery] string? flowKey = null,
        [FromQuery] string? capabilityKey = null,
        [FromQuery] string? platformKey = null)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return IntegrationErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new ListOrganizationRoutingPoliciesQuery(
                organizationId.Value,
                flowKey,
                capabilityKey,
                platformKey),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }

    private static async Task<IResult> UpsertRoutingPolicyAsync(
        [FromBody] UpsertOrganizationRoutingPolicyRequest body,
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return IntegrationErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new UpsertOrganizationRoutingPolicyCommand(
                organizationId.Value,
                body.FlowKey,
                body.CapabilityKey,
                body.PlatformKey,
                body.IsEnabled,
                (body.Candidates ?? Array.Empty<UpsertOrganizationRoutingPolicyCandidateRequest>())
                    .Select(candidate => new UpsertOrganizationRoutingPolicyCandidateInput(
                        candidate.ProviderKey,
                        candidate.Priority,
                        candidate.AllowManagedFallback,
                        candidate.MaxAttempts,
                        candidate.TimeoutMs,
                        candidate.SelectedModel,
                        candidate.IsEnabled))
                    .ToArray()),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }
}

public sealed record ConfigureOrganizationIntegrationRequest(
    IntegrationConnectionMode ConnectionMode,
    bool Enabled,
    string? SelectedAiModel,
    IReadOnlyList<IntegrationCredentialInput>? Credentials);

public sealed record IntegrationCredentialInput(string Key, string? Value);

public sealed record SetDefaultAiIntegrationRequest(string ProviderKey, string? Model);

public sealed record UpsertOrganizationRoutingPolicyRequest(
    string FlowKey,
    string CapabilityKey,
    string? PlatformKey,
    bool IsEnabled,
    IReadOnlyList<UpsertOrganizationRoutingPolicyCandidateRequest>? Candidates);

public sealed record UpsertOrganizationRoutingPolicyCandidateRequest(
    string ProviderKey,
    int Priority,
    bool AllowManagedFallback,
    int MaxAttempts,
    int TimeoutMs,
    string? SelectedModel,
    bool IsEnabled);
