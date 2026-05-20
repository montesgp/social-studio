using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.MonthlyObjectives.ListByClient;

public sealed record ListMonthlyObjectivesQuery(
    Guid ClientId,
    Guid OrganizationId) : IRequest<Result<MonthlyObjectivesListResponse>>;
