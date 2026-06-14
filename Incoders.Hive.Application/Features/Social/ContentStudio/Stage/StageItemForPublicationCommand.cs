using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Stage;

/// <summary>
/// Stages an approved item for publication: Approved → ReadyForPublication, emits the versioned
/// handoff payload, then ReadyForPublication → HandedOff on success (CS-09). Never publishes.
/// </summary>
public sealed record StageItemForPublicationCommand(Guid OrganizationId, Guid ItemId, Guid ApprovedByUserId)
    : IRequest<Result<ContentItemResponse>>;

public sealed class StageItemForPublicationCommandHandler
    : IRequestHandler<StageItemForPublicationCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IPublicationHandoff _publicationHandoff;
    private readonly IClock _clock;

    public StageItemForPublicationCommandHandler(
        IContentItemRepository items,
        IPublicationHandoff publicationHandoff,
        IClock clock)
    {
        _items = items;
        _publicationHandoff = publicationHandoff;
        _clock = clock;
    }

    public async Task<Result<ContentItemResponse>> Handle(StageItemForPublicationCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var now = _clock.UtcNow;

        var stage = item.StageForPublication(now);
        if (stage.IsFailure)
        {
            return Result.Failure<ContentItemResponse>(stage.Error);
        }

        var payload = new PublicationHandoffPayload(
            PublicationHandoffPayload.CurrentSchemaVersion,
            item.OrganizationId,
            item.ClientId,
            item.Id.Value,
            item.Origin,
            item.Kind.FormatKey,
            item.Draft.TargetChannel,
            item.Draft.Text,
            item.Draft.Assets,
            SchedulingHint: null,
            ApprovedAtUtc: now,
            ApprovedByUserId: item.ReviewerUserId ?? request.ApprovedByUserId);

        var handoff = await _publicationHandoff.HandoffAsync(payload, ct);
        if (handoff.IsFailure)
        {
            // Persist ReadyForPublication so the handoff can be retried without re-approving.
            await _items.UpdateAsync(item, ct);
            return Result.Failure<ContentItemResponse>(handoff.Error);
        }

        item.MarkHandedOff(now);
        await _items.UpdateAsync(item, ct);
        return Result.Success(ContentItemResponse.FromDomain(item));
    }
}
