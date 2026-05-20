using Incoders.Hive.Domain.Social.MonthlyObjectives;

namespace Incoders.Hive.Application.Features.Social.MonthlyObjectives.GetByClientAndMonth;

public sealed record MonthlyObjectiveResponse(
    Guid Id,
    Guid ClientId,
    Guid OrganizationId,
    int Year,
    int Month,
    string Objectives,
    IReadOnlyList<string> Kpis,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static MonthlyObjectiveResponse FromDomain(MonthlyObjective objective) =>
        new(
            objective.Id.Value,
            objective.ClientId,
            objective.OrganizationId,
            objective.Year,
            objective.Month,
            objective.Objectives,
            objective.Kpis,
            objective.CreatedAtUtc,
            objective.UpdatedAtUtc);
}
