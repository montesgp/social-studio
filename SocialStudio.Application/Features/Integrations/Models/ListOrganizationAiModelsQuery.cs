using SocialStudio.Application.Abstractions.Integrations;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Integrations;
using MediatR;

namespace SocialStudio.Application.Features.Integrations.Models;

public sealed record ListOrganizationAiModelsQuery(
    Guid OrganizationId,
    string ProviderKey)
    : IRequest<Result<OrganizationAiModelsResponse>>;

public sealed class ListOrganizationAiModelsHandler
    : IRequestHandler<ListOrganizationAiModelsQuery, Result<OrganizationAiModelsResponse>>
{
    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationAiModelRepository _models;

    public ListOrganizationAiModelsHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationAiModelRepository models)
    {
        _providers = providers;
        _models = models;
    }

    public async Task<Result<OrganizationAiModelsResponse>> Handle(
        ListOrganizationAiModelsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<OrganizationAiModelsResponse>(IntegrationErrors.OrganizationRequired);
        }

        var provider = await _providers.GetByKeyAsync(request.ProviderKey, cancellationToken);
        if (provider is null)
        {
            return Result.Failure<OrganizationAiModelsResponse>(IntegrationErrors.ProviderNotFound);
        }

        var rows = await _models.ListByProviderAsync(
            request.OrganizationId,
            provider.Id.Value,
            cancellationToken);

        var responses = rows
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList();

        return Result.Success(new OrganizationAiModelsResponse(
            request.OrganizationId,
            provider.Key,
            rows.Count == 0 ? null : rows.Max(x => x.SyncedAtUtc),
            responses));
    }

    private static OrganizationAiModelResponse ToResponse(OrganizationAiModelRecord row) =>
        new(
            row.Id,
            row.OrganizationId,
            row.OrganizationIntegrationId,
            row.ProviderKey,
            row.ModelId,
            row.DisplayName,
            row.IsAvailable,
            row.SyncedAtUtc);
}

