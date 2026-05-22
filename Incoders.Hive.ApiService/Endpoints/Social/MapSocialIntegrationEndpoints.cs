using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Application.Features.Social.Integrations;
using Incoders.Hive.Domain.Integrations;
using MediatR;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class SocialIntegrationsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapSocialIntegrationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/integrations")
            .WithTags("Social Integrations")
            .RequireAuthorization();

        group.MapGet("/default-ai", GetDefaultAiProviderAsync)
            .WithName("GetSocialDefaultAiProvider")
            .WithSummary("Resolve default AI provider for the current organization through IIntegrationResolver")
            .Produces<SocialDefaultAiProviderResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetDefaultAiProviderAsync(
        ISender sender,
        ICurrentTenant currentTenant,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return IntegrationErrors.OrganizationRequired.ToProblem();
        }

        var result = await sender.Send(
            new GetSocialDefaultAiProviderQuery(organizationId.Value),
            cancellationToken);

        return result.Match(response => (IResult)TypedResults.Ok(response));
    }
}