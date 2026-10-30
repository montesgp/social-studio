using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentStudio;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentStudio.Operations;

public sealed record DuplicateItemCommand(Guid OrganizationId, Guid ItemId)
    : IRequest<Result<ContentItemResponse>>;

public sealed class DuplicateItemCommandHandler
    : IRequestHandler<DuplicateItemCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IContentSetRepository _sets;
    private readonly IClock _clock;

    public DuplicateItemCommandHandler(IContentItemRepository items, IContentSetRepository sets, IClock clock)
    {
        _items = items;
        _sets = sets;
        _clock = clock;
    }

    public async Task<Result<ContentItemResponse>> Handle(DuplicateItemCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var duplicate = item.Duplicate(ContentItemId.New(), _clock.UtcNow);
        await _items.AddRangeAsync(new[] { duplicate }, ct);

        // Surface the copy in the same set so the reviewer can see it. Resolved through the SOURCE
        // item's membership: Origin.SourceRunId is the generation JOB id, not the set's run id
        // (derived from analysisId+clientId), so the old by-run lookup silently found nothing and
        // left every duplicate orphaned — visible in the Bank but impossible to open in Studio.
        var owning = await _sets.GetSetIdsByItemIdsAsync(request.OrganizationId, [item.Id.Value], ct);
        if (owning.TryGetValue(item.Id.Value, out var setId))
        {
            var set = await _sets.GetByIdAsync(request.OrganizationId, new ContentSetId(setId), ct);
            if (set is not null && set.AddMember(duplicate.Id, set.Members.Count).IsSuccess)
            {
                await _sets.UpdateAsync(set, ct);
            }
        }

        return Result.Success(ContentItemResponse.FromDomain(duplicate));
    }
}
