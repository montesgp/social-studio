using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social.ContentStudio;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentStudio;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentStudio.Operations;

/// <summary>Links an item to a content-plan calendar slot (soft association; CP-LINK-01).</summary>
public sealed record LinkItemToPlanCommand(
    Guid OrganizationId,
    Guid ItemId,
    Guid ContentPlanId,
    short PeriodKind,
    int Year,
    int Month,
    Guid CalendarSlotId) : IRequest<Result<ContentItemResponse>>;

public sealed class LinkItemToPlanCommandHandler
    : IRequestHandler<LinkItemToPlanCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IClock _clock;

    public LinkItemToPlanCommandHandler(IContentItemRepository items, IClock clock)
    {
        _items = items;
        _clock = clock;
    }

    public async Task<Result<ContentItemResponse>> Handle(LinkItemToPlanCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        var planContext = new PlanContext(
            request.ContentPlanId,
            new PeriodRef(request.PeriodKind, request.Year, request.Month),
            request.CalendarSlotId);

        var result = item.LinkToPlan(planContext, _clock.UtcNow);
        if (result.IsFailure)
        {
            return Result.Failure<ContentItemResponse>(result.Error);
        }

        await _items.UpdateAsync(item, ct);
        return Result.Success(ContentItemResponse.FromDomain(item));
    }
}

/// <summary>Removes a content-plan association from an item.</summary>
public sealed record UnlinkItemFromPlanCommand(Guid OrganizationId, Guid ItemId)
    : IRequest<Result<ContentItemResponse>>;

public sealed class UnlinkItemFromPlanCommandHandler
    : IRequestHandler<UnlinkItemFromPlanCommand, Result<ContentItemResponse>>
{
    private readonly IContentItemRepository _items;
    private readonly IClock _clock;

    public UnlinkItemFromPlanCommandHandler(IContentItemRepository items, IClock clock)
    {
        _items = items;
        _clock = clock;
    }

    public async Task<Result<ContentItemResponse>> Handle(UnlinkItemFromPlanCommand request, CancellationToken ct)
    {
        var item = await _items.GetByIdAsync(request.OrganizationId, new ContentItemId(request.ItemId), ct);
        if (item is null)
        {
            return Result.Failure<ContentItemResponse>(ContentStudioErrors.ItemNotFound);
        }

        item.UnlinkFromPlan(_clock.UtcNow);
        await _items.UpdateAsync(item, ct);
        return Result.Success(ContentItemResponse.FromDomain(item));
    }
}
