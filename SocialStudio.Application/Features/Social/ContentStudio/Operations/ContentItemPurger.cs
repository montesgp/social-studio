using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Application.Abstractions.Social.Publications;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentStudio;
using SocialStudio.Domain.Social.Publications;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Application.Features.Social.ContentStudio.Operations;

/// <summary>
/// Permanently removes a content item and everything that hangs off it: its stored asset objects,
/// its publication records, its set-membership rows and finally the item row itself.
/// </summary>
/// <remarks>
/// A content item is referenced by soft Guids, never by foreign keys (see
/// <c>ContentStudioItemData</c>: "no navigation to generation/package/plan rows"). Deleting only the
/// item row therefore leaves membership rows pointing at nothing, publication rows pointing at
/// nothing, and asset blobs nobody will ever read again — which is the opposite of reclaiming space.
/// This purger is the single place that knows the full fan-out, so the per-item and bulk delete
/// paths cannot drift apart.
/// </remarks>
public sealed class ContentItemPurger
{
    /// <summary>
    /// Publication states that represent a real, externally-visible outcome. An item carrying any of
    /// these is refused: the purge is irreversible and would erase the record of what actually
    /// reached a network. Cancelled/PublishFailed rows never reached a network, so they are purged
    /// along with the item.
    /// </summary>
    private static readonly PublicationStatus[] LiveStatuses =
    [
        PublicationStatus.Scheduled,
        PublicationStatus.Publishing,
        PublicationStatus.Published,
    ];

    private readonly IContentItemRepository _items;
    private readonly IPublicationRepository _publications;
    private readonly IContentSetRepository _sets;
    private readonly IGeneratedAssetStorage? _assetStorage;
    private readonly ILogger<ContentItemPurger>? _logger;

    public ContentItemPurger(
        IContentItemRepository items,
        IPublicationRepository publications,
        IContentSetRepository sets,
        IGeneratedAssetStorage? assetStorage = null,
        ILogger<ContentItemPurger>? logger = null)
    {
        _items = items;
        _publications = publications;
        _sets = sets;
        _assetStorage = assetStorage;
        _logger = logger;
    }

    /// <summary>
    /// Purges a single item. Fails with <see cref="ContentStudioErrors.ItemNotFound"/> when the item
    /// does not exist and <see cref="ContentStudioErrors.PurgeBlockedByPublication"/> when it still
    /// carries a live publication.
    /// </summary>
    public async Task<Result> PurgeAsync(Guid organizationId, ContentItemId itemId, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(organizationId, itemId, ct);
        if (item is null)
        {
            return Result.Failure(ContentStudioErrors.ItemNotFound);
        }

        var publications = await _publications.ListByContentItemsAsync(organizationId, [itemId.Value], ct);
        if (publications.Any(p => LiveStatuses.Contains(p.Status)))
        {
            return Result.Failure(ContentStudioErrors.PurgeBlockedByPublication);
        }

        // Storage first: a blob we fail to delete is recoverable garbage, whereas an item row deleted
        // before its blobs would lose the only pointer we have to them.
        await DeleteAssetsAsync(item, ct);

        if (publications.Count > 0)
        {
            await _publications.DeleteByContentItemsAsync(organizationId, [itemId.Value], ct);
        }

        await _sets.RemoveItemMembershipsAsync(organizationId, [itemId.Value], ct);
        await _items.DeleteAsync(organizationId, itemId, ct);

        return Result.Success();
    }

    /// <summary>
    /// Deletes the item's own asset objects. Best-effort per asset: storage is an external system and
    /// a blob that is already gone (or a backend that is momentarily unavailable) must not strand the
    /// database rows, which are the part that actually makes the Bank unusable.
    /// </summary>
    private async Task DeleteAssetsAsync(ContentItem item, CancellationToken ct)
    {
        if (_assetStorage is null)
        {
            return;
        }

        foreach (var asset in item.Draft.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.StorageUri))
            {
                continue;
            }

            try
            {
                await _assetStorage.DeleteAsync(asset.StorageUri, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(
                    ex,
                    "Purge of content item {ItemId} could not delete asset {StorageUri}; the database rows were still removed.",
                    item.Id.Value,
                    asset.StorageUri);
            }
        }
    }
}
