using System.Text.Json;
using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Integrations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Integrations;
using MediatR;

namespace Incoders.Hive.Application.Features.Integrations.Actors;

/// <summary>
/// Refreshes the organization's actor catalog from the provider, using the credentials already
/// stored for the connector. The catalog only FEEDS the actor picker — routing keeps resolving
/// from the connector's own actor-mapping configuration, which this command never reads or writes.
/// </summary>
public sealed record SyncOrganizationActorsCommand(
    Guid OrganizationId,
    string ProviderKey)
    : IRequest<Result<OrganizationActorsResponse>>;

public sealed class SyncOrganizationActorsHandler
    : IRequestHandler<SyncOrganizationActorsCommand, Result<OrganizationActorsResponse>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IIntegrationProviderCatalogRepository _providers;
    private readonly IOrganizationIntegrationRepository _integrations;
    private readonly IIntegrationResolver _resolver;
    private readonly IExternalProviderClientFactory _clientFactory;
    private readonly IOrganizationActorRepository _actors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public SyncOrganizationActorsHandler(
        IIntegrationProviderCatalogRepository providers,
        IOrganizationIntegrationRepository integrations,
        IIntegrationResolver resolver,
        IExternalProviderClientFactory clientFactory,
        IOrganizationActorRepository actors,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _providers = providers;
        _integrations = integrations;
        _resolver = resolver;
        _clientFactory = clientFactory;
        _actors = actors;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<OrganizationActorsResponse>> Handle(
        SyncOrganizationActorsCommand request,
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

        // Route by capability, not by category: any provider that can list its actors can sync.
        if (!_clientFactory.Supports<IActorCatalogClient>(provider.Key))
        {
            return Result.Failure<OrganizationActorsResponse>(IntegrationErrors.ExternalProviderUnsupported);
        }

        var integration = await _integrations.GetByOrganizationAndProviderAsync(
            request.OrganizationId,
            provider.Id,
            cancellationToken);

        if (integration is null || integration.Status != IntegrationStatus.Connected)
        {
            return Result.Failure<OrganizationActorsResponse>(IntegrationErrors.IntegrationNotConnected);
        }

        var resolved = await _resolver.ResolveProviderAsync(
            request.OrganizationId,
            provider.Key,
            cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<OrganizationActorsResponse>(resolved.Error);
        }

        IActorCatalogClient client;
        try
        {
            client = _clientFactory.Create<IActorCatalogClient>(
                new ResolvedProviderCredential(
                    provider.Id.Value,
                    provider.Key,
                    resolved.Value.ConnectionMode,
                    resolved.Value.Credentials,
                    resolved.Value.SelectedAiModel));
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<OrganizationActorsResponse>(IntegrationErrors.ExternalProviderUnsupported);
        }

        var listed = await client.ListActorsAsync(cancellationToken);
        if (listed.IsFailure)
        {
            return Result.Failure<OrganizationActorsResponse>(listed.Error);
        }

        var now = _clock.UtcNow;
        var rows = listed.Value
            .Where(x => !string.IsNullOrWhiteSpace(x.ActorId))
            .GroupBy(x => x.ActorId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var actor = group.First();
                var name = actor.Name?.Trim() ?? string.Empty;
                var username = actor.Username?.Trim() ?? string.Empty;
                var displayName = string.IsNullOrWhiteSpace(actor.DisplayName)
                    ? FallbackDisplayName(username, name, actor.ActorId.Trim())
                    : actor.DisplayName.Trim();

                return new OrganizationActorRecord(
                    Guid.NewGuid(),
                    request.OrganizationId,
                    integration.Id.Value,
                    provider.Id.Value,
                    provider.Key,
                    actor.ActorId.Trim(),
                    name,
                    username,
                    displayName,
                    JsonSerializer.Serialize(actor.Metadata, JsonOptions),
                    actor.IsOwned,
                    IsAvailable: true,
                    SyncedAtUtc: now,
                    CreatedAtUtc: now,
                    UpdatedAtUtc: now);
            })
            .ToList();

        await _actors.UpsertManyAsync(rows, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var persisted = await _actors.ListByProviderAsync(
            request.OrganizationId,
            provider.Id.Value,
            cancellationToken);

        var responses = persisted
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(OrganizationActorMapper.ToResponse)
            .ToList();

        return Result.Success(new OrganizationActorsResponse(
            request.OrganizationId,
            provider.Key,
            responses.Count == 0 ? now : responses.Max(x => x.SyncedAtUtc),
            responses));
    }

    private static string FallbackDisplayName(string username, string name, string actorId)
    {
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(name))
        {
            return OrganizationActorMapper.BuildSlug(username, name);
        }

        return string.IsNullOrWhiteSpace(name) ? actorId : name;
    }
}

internal static class OrganizationActorMapper
{
    public static string BuildSlug(string username, string name)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return name.Trim();
        }

        return string.IsNullOrWhiteSpace(name)
            ? username.Trim()
            : $"{username.Trim()}/{name.Trim()}";
    }

    public static OrganizationActorResponse ToResponse(OrganizationActorRecord row) =>
        new(
            row.Id,
            row.OrganizationId,
            row.OrganizationIntegrationId,
            row.ProviderKey,
            row.ActorId,
            row.Name,
            row.Username,
            BuildSlug(row.Username, row.Name),
            row.DisplayName,
            row.IsOwned,
            row.IsAvailable,
            row.SyncedAtUtc);
}
