using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.GetByClientAndMonth;

public sealed record GetClientMonthlyObjectiveQuery(
    Guid ClientId,
    Guid OrganizationId,
    int Year,
    int Month) : IRequest<Result<ClientMonthlyObjectiveResponse>>;
