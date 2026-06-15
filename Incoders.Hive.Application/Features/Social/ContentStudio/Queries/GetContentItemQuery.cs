using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Queries;

public sealed record GetContentItemQuery(Guid OrganizationId, Guid ItemId) : IRequest<Result<ContentItemResponse>>;

public sealed class GetContentItemQueryHandler
    : IRequestHandler<GetContentItemQuery, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;

    public GetContentItemQueryHandler(IContentItemRepository items)
    {
        _items = items;
    }

    public async Task<Result<ContentItemResponse>> Handle(GetContentItemQuery request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        return item is null
            ? Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound)
            : Result.Success(ContentItemResponse.FromDomain(item));
    }
}
