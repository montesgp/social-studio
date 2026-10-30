using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentPlans.Upsert;

/// <summary>
/// Request DTO for a single calendar slot in the upsert command.
/// Day is validated against the period's month at handler time.
/// </summary>
public sealed record CalendarSlotRequest(
    int Day,
    string ContentFormat);

public sealed record UpsertContentPlanCommand(
    Guid ClientId,
    Guid OrganizationId,
    int Year,
    int Month,
    string Objectives,
    IReadOnlyList<string>? Kpis,
    string? ContentStrategyText,
    IReadOnlyList<CalendarSlotRequest>? Slots) : IRequest<Result<ContentPlanResponse>>;
