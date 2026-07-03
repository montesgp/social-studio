using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using MediatR;

namespace Incoders.Hive.Application.Features.Integrations.Models;

public sealed record SyncOrganizationAiModelsCommand(
    Guid OrganizationId,
    string ProviderKey)
    : IRequest<Result<OrganizationAiModelsResponse>>;

public sealed class SyncOrganizationAiModelsHandler
    : IRequestHandler<SyncOrganizationAiModelsCommand, Result<OrganizationAiModelsResponse>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IIntegrationResolver _resolver;
    private readonly IExternalProviderClientFactory _clientFactory;
    private readonly IOrganizationAiModelRepository _models;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public SyncOrganizationAiModelsHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IIntegrationResolver resolver,
        IExternalProviderClientFactory clientFactory,
        IOrganizationAiModelRepository models,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _providers = providers;
        _integrations = integrations;
        _resolver = resolver;
        _clientFactory = clientFactory;
        _models = models;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<OrganizationAiModelsResponse>> Handle(
        SyncOrganizationAiModelsCommand request,
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

        // Route by capability, not by category: any provider that exposes a model catalog client
        // (text AI models OR media-generation backends such as ComfyUI) can sync its live model list.
        if (!_clientFactory.Supports<IAiModelCatalogClient>(provider.Key))
        {
            return Result.Failure<OrganizationAiModelsResponse>(IntegrationErrors.ExternalProviderUnsupported);
        }

        var integration = await _integrations.GetByOrganizationAndProviderAsync(
            request.OrganizationId,
            provider.Id,
            cancellationToken);

        if (integration is null || integration.Status != IntegrationStatus.Connected)
        {
            return Result.Failure<OrganizationAiModelsResponse>(IntegrationErrors.IntegrationNotConnected);
        }

        var resolved = await _resolver.ResolveProviderAsync(
            request.OrganizationId,
            provider.Key,
            cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<OrganizationAiModelsResponse>(resolved.Error);
        }

        IAiModelCatalogClient client;
        try
        {
            client = _clientFactory.Create<IAiModelCatalogClient>(
                new ResolvedProviderCredential(
                    provider.Id.Value,
                    provider.Key,
                    resolved.Value.ConnectionMode,
                    resolved.Value.Credentials,
                    resolved.Value.SelectedAiModel));
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<OrganizationAiModelsResponse>(IntegrationErrors.ExternalProviderUnsupported);
        }

        var listed = await client.ListModelsAsync(cancellationToken);
        if (listed.IsFailure)
        {
            return Result.Failure<OrganizationAiModelsResponse>(listed.Error);
        }

        var now = _clock.UtcNow;
        var rows = listed.Value
            .Where(x => !string.IsNullOrWhiteSpace(x.ModelId))
            .GroupBy(x => x.ModelId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var model = group.First();
                return new OrganizationAiModelRecord(
                    Guid.NewGuid(),
                    request.OrganizationId,
                    integration.Id.Value,
                    provider.Id.Value,
                    provider.Key,
                    model.ModelId.Trim(),
                    string.IsNullOrWhiteSpace(model.DisplayName) ? model.ModelId.Trim() : model.DisplayName.Trim(),
                    "[]",
                    JsonSerializer.Serialize(model.Metadata, JsonOptions),
                    IsAvailable: true,
                    SyncedAtUtc: now,
                    CreatedAtUtc: now,
                    UpdatedAtUtc: now);
            })
            .ToList();

        await _models.UpsertManyAsync(rows, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var persisted = await _models.ListByProviderAsync(
            request.OrganizationId,
            provider.Id.Value,
            cancellationToken);

        var responses = persisted
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList();

        return Result.Success(new OrganizationAiModelsResponse(
            request.OrganizationId,
            provider.Key,
            responses.Count == 0 ? now : responses.Max(x => x.SyncedAtUtc),
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
