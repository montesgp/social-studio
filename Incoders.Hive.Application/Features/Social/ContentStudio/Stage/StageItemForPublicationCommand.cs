using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Stage;

/// <summary>
/// Marks an approved item ready for publication: Approved → ReadyForPublication. Never publishes,
/// and never leaves the editorial axis.
/// </summary>
/// <remarks>
/// This used to emit a versioned payload through <c>IPublicationHandoff</c> and then move the item
/// on to <c>HandedOff</c> (CS-09). ADR 0005 superseded that model: the publication axis is now the
/// <c>Publication</c> aggregate, which pulls an item by id. Because <c>HandedOff</c> is terminal, the
/// old behavior ejected staged pieces from the Planner and made them unpublishable, so staging now
/// stops at <see cref="ReviewState.ReadyForPublication"/> — a state the Planner and the publish gate
/// both understand.
/// </remarks>
public sealed record StageItemForPublicationCommand(Guid OrganizationId, Guid ItemId)
    : IRequest<Result<ContentItemResponse>>;

public sealed class StageItemForPublicationCommandHandler
    : IRequestHandler<StageItemForPublicationCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IClock _clock;

    public StageItemForPublicationCommandHandler(IContentItemRepository items, IClock clock)
    {
        _items = items;
        _clock = clock;
    }

    public async Task<Result<ContentItemResponse>> Handle(StageItemForPublicationCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var stage = item.StageForPublication(_clock.UtcNow);
        if (stage.IsFailure)
        {
            return Result.Failure<ContentItemResponse>(stage.Error);
        }

        await _items.UpdateAsync(item, ct);
        return Result.Success(ContentItemResponse.FromDomain(item));
    }
}
