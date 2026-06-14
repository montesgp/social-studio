using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

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

        // Surface the copy in the same run set so the reviewer can see it.
        var set = await _sets.GetByRunAsync(request.OrganizationId, item.Origin.SourceRunId, ct);
        if (set is not null && set.AddMember(duplicate.Id, set.Members.Count).IsSuccess)
        {
            await _sets.UpdateAsync(set, ct);
        }

        return Result.Success(ContentItemResponse.FromDomain(duplicate));
    }
}
