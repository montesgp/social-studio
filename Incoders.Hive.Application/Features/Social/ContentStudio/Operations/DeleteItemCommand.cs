using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

/// <summary>
/// Permanently removes an item and everything related to it (assets, publications, set membership).
/// This is a purge, not a state change — use <see cref="DiscardItemCommand"/> to retire an item
/// while keeping it in the Bank.
/// </summary>
public sealed record DeleteItemCommand(Guid OrganizationId, Guid ItemId) : IRequest<Result>;

public sealed class DeleteItemCommandHandler : IRequestHandler<DeleteItemCommand, Result>
{
    private readonly ContentItemPurger _purger;

    public DeleteItemCommandHandler(ContentItemPurger purger)
    {
        _purger = purger;
    }

    public Task<Result> Handle(DeleteItemCommand request, CancellationToken ct) =>
        _purger.PurgeAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
}
