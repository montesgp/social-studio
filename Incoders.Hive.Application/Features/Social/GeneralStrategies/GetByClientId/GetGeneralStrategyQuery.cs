using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.GeneralStrategies.GetByClientId;

public sealed record GetGeneralStrategyQuery(
    Guid ClientId,
    Guid OrganizationId) : IRequest<Result<GeneralStrategyResponse>>;
