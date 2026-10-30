using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.ContentPlans;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentPlans.Upsert;

public sealed class UpsertContentPlanCommandHandler
    : IRequestHandler<UpsertContentPlanCommand, Result<ContentPlanResponse>>
{
    private readonly IContentPlanRepository _contentPlans;
    private readonly IClock _clock;

    public UpsertContentPlanCommandHandler(
        IContentPlanRepository contentPlans,
        IClock clock)
    {
        _contentPlans = contentPlans;
        _clock = clock;
    }

    public async Task<Result<ContentPlanResponse>> Handle(
        UpsertContentPlanCommand request,
        CancellationToken cancellationToken)
    {
        // Validate and construct Period
        var periodResult = Period.Monthly(request.Year, request.Month);
        if (periodResult.IsFailure)
        {
            return Result.Failure<ContentPlanResponse>(periodResult.Error);
        }

        var period = periodResult.Value;
        var now = _clock.UtcNow;

        var existing = await _contentPlans.GetByClientAndPeriodAsync(
            request.OrganizationId, request.ClientId, period, cancellationToken);

        if (existing is null)
        {
            // CREATE path
            var createResult = ContentPlan.Create(
                ContentPlanId.New(),
                request.ClientId,
                request.OrganizationId,
                period,
                request.Objectives,
                request.Kpis,
                now);

            if (createResult.IsFailure)
            {
                return Result.Failure<ContentPlanResponse>(createResult.Error);
            }

            var plan = createResult.Value;

            if (request.ContentStrategyText is not null)
            {
                plan.UpsertContentStrategy(request.ContentStrategyText, Provenance.Manual(now), now);
            }

            if (request.Slots is { Count: > 0 })
            {
                var slotInputs = MapSlotRequests(request.Slots, now);
                var slotsResult = plan.ReplaceCalendarSlots(slotInputs, now);
                if (slotsResult.IsFailure)
                {
                    return Result.Failure<ContentPlanResponse>(slotsResult.Error);
                }
            }

            await _contentPlans.UpsertAsync(plan, cancellationToken);

            return Result.Success(ContentPlanResponse.FromDomain(plan));
        }

        // UPDATE path — upsert objective
        var objectiveResult = existing.UpsertObjective(request.Objectives, request.Kpis, now, now);
        if (objectiveResult.IsFailure)
        {
            return Result.Failure<ContentPlanResponse>(objectiveResult.Error);
        }

        if (request.ContentStrategyText is not null)
        {
            existing.UpsertContentStrategy(request.ContentStrategyText, Provenance.Manual(now), now);
        }

        if (request.Slots is not null)
        {
            var slotInputs = MapSlotRequests(request.Slots, now);
            var slotsResult = existing.ReplaceCalendarSlots(slotInputs, now);
            if (slotsResult.IsFailure)
            {
                return Result.Failure<ContentPlanResponse>(slotsResult.Error);
            }
        }

        await _contentPlans.UpsertAsync(existing, cancellationToken);

        return Result.Success(ContentPlanResponse.FromDomain(existing));
    }

    private static IReadOnlyList<CalendarSlotInput> MapSlotRequests(
        IReadOnlyList<CalendarSlotRequest> slots, DateTime nowUtc) =>
        slots
            .Select(s => new CalendarSlotInput(s.Day, s.ContentFormat, Provenance.Manual(nowUtc)))
            .ToList();
}
