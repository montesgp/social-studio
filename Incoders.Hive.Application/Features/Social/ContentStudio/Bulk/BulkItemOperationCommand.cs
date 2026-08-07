using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Application.Features.Social.ContentStudio.Operations;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Bulk;

/// <summary>Bulk operations available over a selection of items.</summary>
public enum BulkItemOperation
{
    Approve,
    Reject,
    Discard,
    Delete,
}

/// <summary>Per-item outcome of a bulk operation (partial-success semantics).</summary>
public sealed record BulkItemOutcome(Guid ItemId, bool Succeeded, string? ErrorCode);

public sealed record BulkOperationResult(
    int Total,
    int Succeeded,
    int Failed,
    IReadOnlyList<BulkItemOutcome> Outcomes);

/// <summary>
/// Applies a single operation across a selection of items as an application-layer fan-out over the
/// per-item domain operations, returning a partial-success result (CS bulk, design §9). Individual
/// item failures (illegal transition, not found) do not abort the batch — they are reported per item.
/// </summary>
public sealed record BulkItemOperationCommand(
    Guid OrganizationId,
    Guid ReviewerUserId,
    IReadOnlyList<Guid> ItemIds,
    BulkItemOperation Operation,
    string? Reason) : IRequest<Result<BulkOperationResult>>;

public sealed class BulkItemOperationCommandHandler
    : IRequestHandler<BulkItemOperationCommand, Result<BulkOperationResult>>
{
    private readonly IContentItemRepository _items;
    private readonly ContentItemPurger _purger;
    private readonly IClock _clock;

    public BulkItemOperationCommandHandler(
        IContentItemRepository items, ContentItemPurger purger, IClock clock)
    {
        _items = items;
        _purger = purger;
        _clock = clock;
    }

    public async Task<Result<BulkOperationResult>> Handle(BulkItemOperationCommand request, CancellationToken ct)
    {
        if (request.ItemIds.Count == 0)
        {
            return Result.Failure<BulkOperationResult>(
                ContentStudioErrors.Validation("No items selected for the bulk operation."));
        }

        if (request.Operation == BulkItemOperation.Reject && string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Failure<BulkOperationResult>(ContentStudioErrors.RejectionReasonRequired);
        }

        var now = _clock.UtcNow;
        var outcomes = new List<BulkItemOutcome>(request.ItemIds.Count);

        foreach (var rawId in request.ItemIds.Distinct())
        {
            var id = new ContentItemId(rawId);

            if (request.Operation == BulkItemOperation.Delete)
            {
                // Delete is a full purge (assets, publications, set membership, row). It can legally
                // fail per item — a piece with a live publication is refused — so it reports through
                // the same partial-success outcome list as every other operation instead of always
                // claiming success.
                var purge = await _purger.PurgeAsync(request.OrganizationId, id, ct);
                outcomes.Add(purge.IsSuccess
                    ? new BulkItemOutcome(rawId, Succeeded: true, ErrorCode: null)
                    : new BulkItemOutcome(rawId, Succeeded: false, ErrorCode: purge.Error.Code));
                continue;
            }

            var item = await _items.GetByIdAsync(request.OrganizationId, id, ct);
            if (item is null)
            {
                outcomes.Add(new BulkItemOutcome(rawId, false, ContentStudioErrors.ItemNotFound.Code));
                continue;
            }

            var result = request.Operation switch
            {
                BulkItemOperation.Approve => item.Approve(request.ReviewerUserId, now),
                BulkItemOperation.Reject => item.Reject(request.ReviewerUserId, request.Reason!, now),
                BulkItemOperation.Discard => item.Discard(now),
                _ => Result.Failure(ContentStudioErrors.Validation("Unsupported bulk operation.")),
            };

            if (result.IsFailure)
            {
                outcomes.Add(new BulkItemOutcome(rawId, false, result.Error.Code));
                continue;
            }

            await _items.UpdateAsync(item, ct);
            outcomes.Add(new BulkItemOutcome(rawId, true, null));
        }

        var succeeded = outcomes.Count(o => o.Succeeded);
        return Result.Success(new BulkOperationResult(
            Total: outcomes.Count,
            Succeeded: succeeded,
            Failed: outcomes.Count - succeeded,
            Outcomes: outcomes));
    }
}
