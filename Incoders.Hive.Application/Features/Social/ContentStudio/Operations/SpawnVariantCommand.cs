using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentStudio;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentStudio.Operations;

/// <summary>
/// Creates a variant of an item in the same variant group (a non-primary alternative the reviewer
/// can edit independently) and adds it to the item's run set so it appears alongside the original.
/// </summary>
public sealed record SpawnVariantCommand(Guid OrganizationId, Guid ItemId) : IRequest<Result<ContentItemResponse>>;

public sealed class SpawnVariantCommandHandler
    : IRequestHandler<SpawnVariantCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IContentSetRepository _sets;
    private readonly IClock _clock;

    public SpawnVariantCommandHandler(IContentItemRepository items, IContentSetRepository sets, IClock clock)
    {
        _items = items;
        _sets = sets;
        _clock = clock;
    }

    public async Task<Result<ContentItemResponse>> Handle(SpawnVariantCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var variant = item.SpawnVariant(ContentItemId.New(), _clock.UtcNow);
        await _items.AddRangeAsync(new[] { variant }, ct);
        await AddToRunSetAsync(request.OrganizationId, item.Origin.SourceRunId, variant.Id, ct);

        return Result.Success(ContentItemResponse.FromDomain(variant));
    }

    private async Task AddToRunSetAsync(Guid organizationId, Guid sourceRunId, ContentItemId newItemId, CancellationToken ct)
    {
        var set = await _sets.GetByRunAsync(organizationId, sourceRunId, ct);
        if (set is null)
        {
            return;
        }

        if (set.AddMember(newItemId, set.Members.Count).IsSuccess)
        {
            await _sets.UpdateAsync(set, ct);
        }
    }
}
