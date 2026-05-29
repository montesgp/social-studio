using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.ListByClient;

public sealed record ListClientMonthlyObjectivesQuery(
    Guid ClientId,
    Guid OrganizationId) : IRequest<Result<ClientMonthlyObjectivesListResponse>>;
