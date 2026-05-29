using Incoders.Hive.Domain.Social.ClientMonthlyObjectives;

namespace Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.GetByClientAndMonth;

public sealed record ClientMonthlyObjectiveResponse(
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
    public static ClientMonthlyObjectiveResponse FromDomain(ClientMonthlyObjective objective) =>
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
