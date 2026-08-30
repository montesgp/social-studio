using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

/// <summary>
/// Brings a piece back into review (<see cref="ReviewState.InReview"/>) so it can be edited again.
/// </summary>
/// <remarks>
/// The transition table already allows <c>Discarded → InReview</c> ("recover") and
/// <c>Rejected → InReview</c>, but nothing exposed it: a discarded piece could be created from the
/// UI and never brought back, so Studio rendered it permanently read-only with no explanation and no
/// way out. Editing is blocked in those states because <c>Edited</c> is not reachable from them —
/// reopening is the domain-sanctioned step that makes editing legal again.
/// </remarks>
public sealed record ReopenItemCommand(Guid OrganizationId, Guid ItemId)
    : IRequest<Result<ContentItemResponse>>;

public sealed class ReopenItemCommandHandler
    : IRequestHandler<ReopenItemCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IClock _clock;

    public ReopenItemCommandHandler(IContentItemRepository items, IClock clock)
    {
        _items = items;
        _clock = clock;
    }

    public async Task<Result<ContentItemResponse>> Handle(ReopenItemCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var result = item.BeginReview(_clock.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<ContentItemResponse>(result.Error);
        }

        await _items.UpdateAsync(item, ct);
        return Result.Success(ContentItemResponse.FromDomain(item));
    }
}
