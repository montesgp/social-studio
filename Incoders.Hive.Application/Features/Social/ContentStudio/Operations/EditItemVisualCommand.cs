using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

/// <summary>An asset reference supplied by a visual edit (image replacement / background swap).</summary>
public sealed record VisualAssetInput(string Role, string StorageUri, string Mime);

/// <summary>
/// Edits the visual draft of a piece: composition markup + asset references (image replacement,
/// background swap, basic layout/branding adjustments). Sets the item to Edited. The immutable
/// source result is never touched; pixel re-render happens later in the generation/export pipeline.
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

    public EditItemVisualCommandHandler(IContentItemRepository items, IClock clock)
    {
        _items = items;
        _clock = clock;
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

        var result = item.EditVisual(request.VisualSpecMarkup, assets, _clock.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<ContentItemResponse>(result.Error);
        }

        await _items.UpdateAsync(item, ct);
        return Result.Success(ContentItemResponse.FromDomain(item));
    }
}
