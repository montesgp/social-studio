using SocialStudio.Domain.Social.ContentPlans;

namespace SocialStudio.Application.Features.Social.ContentPlans;

public sealed record ProvenanceDto(
    string Source,
    string Status,
    DateTime? GeneratedAtUtc,
    string? PromptVersion)
{
    public static ProvenanceDto FromDomain(Provenance p) =>
        new(p.Source.ToString(), p.Status.ToString(), p.GeneratedAtUtc, p.PromptVersion);
}

public sealed record CalendarSlotDto(
    Guid Id,
    int Day,
    string ContentFormat,
    int SortOrder,
    Guid? ProducedPackageId,
    ProvenanceDto Provenance)
{
    public static CalendarSlotDto FromDomain(CalendarSlot s) =>
        new(s.Id.Value, s.Day, s.ContentFormat, s.SortOrder, s.ProducedPackageId,
            ProvenanceDto.FromDomain(s.Provenance));
}

public sealed record ContentPlanResponse(
    Guid Id,
    Guid ClientId,
    Guid OrganizationId,
    string PeriodKind,
    int Year,
    int Month,
    string ObjectiveText,
    IReadOnlyList<string> Kpis,
    ProvenanceDto ObjectiveProvenance,
    string? ContentStrategyText,
    ProvenanceDto ContentStrategyProvenance,
    IReadOnlyList<CalendarSlotDto> Slots,
    ProvenanceDto CalendarProvenance,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static ContentPlanResponse FromDomain(ContentPlan plan) =>
        new(
            plan.Id.Value,
            plan.ClientId,
            plan.OrganizationId,
            plan.Period.Kind.ToString(),
            plan.Period.Year,
            plan.Period.Month,
            plan.Objective.Objectives,
            plan.Objective.Kpis,
            ProvenanceDto.FromDomain(plan.Objective.Provenance),
            plan.ContentStrategy.StrategyText,
            ProvenanceDto.FromDomain(plan.ContentStrategy.Provenance),
            plan.Calendar.Slots.Select(CalendarSlotDto.FromDomain).ToList(),
            ProvenanceDto.FromDomain(plan.Calendar.Provenance),
            plan.CreatedAtUtc,
            plan.UpdatedAtUtc);
}

public sealed record ContentPlanSummaryResponse(
    Guid Id,
    Guid ClientId,
    string PeriodKind,
    int Year,
    int Month,
    ProvenanceDto ObjectiveProvenance,
    DateTime UpdatedAtUtc)
{
    public static ContentPlanSummaryResponse FromDomain(ContentPlan plan) =>
        new(
            plan.Id.Value,
            plan.ClientId,
            plan.Period.Kind.ToString(),
            plan.Period.Year,
            plan.Period.Month,
            ProvenanceDto.FromDomain(plan.Objective.Provenance),
            plan.UpdatedAtUtc);
}
