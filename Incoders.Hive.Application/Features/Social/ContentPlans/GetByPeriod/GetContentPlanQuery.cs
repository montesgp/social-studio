using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentPlans.GetByPeriod;

public sealed record GetContentPlanQuery(
    Guid ClientId,
    Guid OrganizationId,
    int Year,
    int Month) : IRequest<Result<ContentPlanResponse>>;
