using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using Incoders.Hive.Domain.Social.Publications;

namespace Incoders.Hive.Application.Features.Social.Publications;

/// <summary>
/// Shared connector-invocation logic used by <c>PublishItemNowCommand</c> and
/// <c>RetryPublicationCommand</c> (design §7): resolves the two-level credentials, resolves the item's
/// visual assets into publicly-fetchable URLs (via <see cref="IGeneratedAssetStorage.TryGetPublicUrl"/>
/// — ALL assets, so a multi-asset item becomes an Instagram carousel), invokes the connector, and
/// returns the provider's external reference. The caller owns persist-before-call ordering and applying
/// the resulting <c>MarkPublished</c>/<c>MarkFailed</c> transition.
/// </summary>
public sealed class PublicationPublishAttemptRunner
{
    private readonly IPublicationDestinationResolver _destinationResolver;
    private readonly IPublicationConnectorSelector _connectorSelector;
    private readonly IGeneratedAssetStorage _assetStorage;

    public PublicationPublishAttemptRunner(
        IPublicationDestinationResolver destinationResolver,
        IPublicationConnectorSelector connectorSelector,
        IGeneratedAssetStorage assetStorage)
    {
        _destinationResolver = destinationResolver;
        _connectorSelector = connectorSelector;
        _assetStorage = assetStorage;
    }

    public async Task<Result<string>> RunAsync(
        Guid organizationId, ContentItem item, PublicationDestination destination, CancellationToken ct)
    {
        var credentials = await _destinationResolver.ResolveAsync(organizationId, destination, ct);
        if (credentials.IsFailure)
        {
            return Result.Failure<string>(credentials.Error);
        }

        var connector = _connectorSelector.For(destination.NetworkKey);
        if (connector.IsFailure)
        {
            return Result.Failure<string>(connector.Error);
        }

        var assets = ResolveAssets(item);
        if (assets.IsFailure)
        {
            return Result.Failure<string>(assets.Error);
        }

        var request = new ResolvedPublicationRequest(
            organizationId,
            item.ClientId,
            item.Id.Value,
            credentials.Value.ExternalAccountId,
            credentials.Value.AccessToken,
            credentials.Value.NetworkKey,
            item.Draft.Caption?.Value,
            assets.Value);

        var published = await connector.Value.PublishAsync(request, ct);
        return published.IsFailure
            ? Result.Failure<string>(published.Error)
            : Result.Success(published.Value.ExternalReference);
    }

    /// <summary>Resolves EVERY draft asset to a public URL — not just the first — so a native connector
    /// (e.g. the Instagram Graph adapter) can publish a carousel when there is more than one.
    /// <c>Assets.Count == 0</c> is a structured failure, never an empty-carousel attempt.</summary>
    private Result<IReadOnlyList<PublicationAssetRef>> ResolveAssets(ContentItem item)
    {
        if (item.Draft.Assets.Count == 0)
        {
            return Result.Failure<IReadOnlyList<PublicationAssetRef>>(
                PublicationErrors.Validation("The item has no publishable assets."));
        }

        var resolved = new List<PublicationAssetRef>(item.Draft.Assets.Count);
        foreach (var asset in item.Draft.Assets)
        {
            var url = _assetStorage.TryGetPublicUrl(asset.StorageUri);
            if (url.IsFailure)
            {
                return Result.Failure<IReadOnlyList<PublicationAssetRef>>(url.Error);
            }

            resolved.Add(new PublicationAssetRef(url.Value, asset.Mime));
        }

        return Result.Success<IReadOnlyList<PublicationAssetRef>>(resolved);
    }
}
