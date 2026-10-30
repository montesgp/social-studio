using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using MediatR;

namespace SocialStudio.Application.Features.Integrations.Actors;

/// <summary>Reads the last synced actor catalog. Never contacts the provider.</summary>
public sealed record ListOrganizationActorsQuery(
    Guid OrganizationId,
    string ProviderKey)
    : IRequest<Result<OrganizationActorsResponse>>;

public sealed class ListOrganizationActorsHandler
    : IRequestHandler<ListOrganizationActorsQuery, Result<OrganizationActorsResponse>>
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationActorRepository _actors;

    public ListOrganizationActorsHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationActorRepository actors)
    {
        _providers = providers;
        _actors = actors;
    }

    public async Task<Result<OrganizationActorsResponse>> Handle(
        ListOrganizationActorsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<OrganizationActorsResponse>(IntegrationErrors.OrganizationRequired);
        }

        var provider = await _providers.GetByKeyAsync(request.ProviderKey, cancellationToken);
        if (provider is null)
        {
            return Result.Failure<OrganizationActorsResponse>(IntegrationErrors.ProviderNotFound);
        }

        var rows = await _actors.ListByProviderAsync(
            request.OrganizationId,
            provider.Id.Value,
            cancellationToken);

        var responses = rows
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(OrganizationActorMapper.ToResponse)
            .ToList();

        return Result.Success(new OrganizationActorsResponse(
            request.OrganizationId,
            provider.Key,
            rows.Count == 0 ? null : rows.Max(x => x.SyncedAtUtc),
            responses));
    }
}
