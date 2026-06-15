using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

public sealed record DeleteItemCommand(Guid OrganizationId, Guid ItemId) : IRequest<Result>;

public sealed class DeleteItemCommandHandler : IRequestHandler<DeleteItemCommand, Result>
{
    private readonly IContentItemRepository _items;

    public DeleteItemCommandHandler(IContentItemRepository items)
    {
        _items = items;
    }

    public async Task<Result> Handle(DeleteItemCommand request, CancellationToken ct)
    {
        await _items.DeleteAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        return Result.Success();
    }
}
