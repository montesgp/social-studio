using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.MonthlyObjectives.GetByClientAndMonth;

public sealed record GetMonthlyObjectiveQuery(
    Guid ClientId,
    Guid OrganizationId,
    int Year,
    int Month) : IRequest<Result<MonthlyObjectiveResponse>>;
