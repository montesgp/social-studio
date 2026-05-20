using Incoders.Hive.Application.Features.Social.MonthlyObjectives.GetByClientAndMonth;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.MonthlyObjectives.Upsert;

public sealed record UpsertMonthlyObjectiveCommand(
    Guid ClientId,
    Guid OrganizationId,
    int Year,
    int Month,
    string Objectives,
    IReadOnlyList<string>? Kpis) : IRequest<Result<MonthlyObjectiveResponse>>;
