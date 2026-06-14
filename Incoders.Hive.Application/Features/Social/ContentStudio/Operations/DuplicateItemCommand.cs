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
    private readonly IClock _clock;

    public DuplicateItemCommandHandler(IContentItemRepository items, IClock clock)
    {
        _items = items;
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

        return Result.Success(ContentItemResponse.FromDomain(duplicate));
    }
}
