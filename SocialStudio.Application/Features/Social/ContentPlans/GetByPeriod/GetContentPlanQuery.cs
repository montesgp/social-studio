using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentPlans.GetByPeriod;

public sealed record GetContentPlanQuery(
    Guid ClientId,
    Guid OrganizationId,
    int Year,
    int Month) : IRequest<Result<ContentPlanResponse>>;
