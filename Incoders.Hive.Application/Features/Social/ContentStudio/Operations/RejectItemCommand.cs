using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

public sealed record RejectItemCommand(Guid OrganizationId, Guid ItemId, Guid ReviewerUserId, string Reason)
    : IRequest<Result<ContentItemResponse>>;

public sealed class RejectItemCommandHandler
    : IRequestHandler<RejectItemCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IClock _clock;

    public RejectItemCommandHandler(IContentItemRepository items, IClock clock)
    {
        _items = items;
        _clock = clock;
    }

    public async Task<Result<ContentItemResponse>> Handle(RejectItemCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var result = item.Reject(request.ReviewerUserId, request.Reason, _clock.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<ContentItemResponse>(result.Error);
        }

        await _items.UpdateAsync(item, ct);
        return Result.Success(ContentItemResponse.FromDomain(item));
    }
}
