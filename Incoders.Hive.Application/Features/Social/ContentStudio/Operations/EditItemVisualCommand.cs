using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Pipeline;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentGeneration;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

/// <summary>An asset reference supplied by a visual edit (image replacement / background swap).</summary>
public sealed record VisualAssetInput(string Role, string StorageUri, string Mime);

/// <summary>
/// Edits the visual draft of a piece: composition markup + asset references (image replacement,
/// background swap, basic layout/branding adjustments). Sets the item to Edited. The immutable
/// source result is never touched.
///
/// <para>
/// Step 2 (fidelity): the edited markup is re-rendered server-side and the FULL flattened PNG is
/// stored on each envelope (<c>FlattenedImageRef</c>), so the PPTX export reproduces ANY edit —
/// text, fonts, colors, moves AND non-slot decorations/lines/buttons. That render now happens in a
/// background worker (<see cref="IVisualFlattenQueue"/>) rather than inside this request: it drives
/// headless Chromium once per envelope behind a two-slot semaphore, which made an interactive save
/// take tens of seconds and fail as "Failed to fetch". The save commits the authored markup and
/// returns; the preview catches up moments later. Flattening stays best-effort — any failure leaves
/// the legacy background + text-box export path in place.
/// </para>
/// </summary>
public sealed record EditItemVisualCommand(
    Guid OrganizationId,
    Guid ItemId,
    string? VisualSpecMarkup,
    IReadOnlyList<VisualAssetInput> Assets) : IRequest<Result<ContentItemResponse>>;

public sealed class EditItemVisualCommandHandler
    : IRequestHandler<EditItemVisualCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IClock _clock;
    private readonly IVisualFlattenQueue? _flattenQueue;

    public EditItemVisualCommandHandler(
        IContentItemRepository items,
        IClock clock,
        IVisualFlattenQueue? flattenQueue = null)
    {
        _items = items;
        _clock = clock;
        _flattenQueue = flattenQueue;
    }

    public async Task<Result<ContentItemResponse>> Handle(EditItemVisualCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var assets = (request.Assets ?? [])
            .Select(a => new ContentAssetRef(a.Role, a.StorageUri, a.Mime))
            .ToList();

        // Persist the operator's markup AS AUTHORED and return. The flattened preview is derived and
        // is rebuilt by a background worker — see IVisualFlattenQueue for why it cannot run here.
        var result = item.EditVisual(request.VisualSpecMarkup, assets, _clock.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<ContentItemResponse>(result.Error);
        }

        await _items.UpdateAsync(item, ct);

        // Enqueued only AFTER the save is committed, so the worker can never read a half-written
        // item, and a dropped enqueue costs a stale preview rather than the edit itself.
        _flattenQueue?.Enqueue(new VisualFlattenRequest(request.OrganizationId, request.ItemId));

        return Result.Success(ContentItemResponse.FromDomain(item));
    }
}
