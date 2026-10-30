using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.GeneralStrategies.GetByClientId;

public sealed record GetGeneralStrategyQuery(
    Guid ClientId,
    Guid OrganizationId) : IRequest<Result<GeneralStrategyResponse>>;
