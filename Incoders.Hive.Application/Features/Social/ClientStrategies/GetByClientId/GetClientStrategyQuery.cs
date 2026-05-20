using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientStrategies.GetByClientId;

public sealed record GetClientStrategyQuery(
    Guid ClientId,
    Guid OrganizationId) : IRequest<Result<ClientStrategyResponse>>;
