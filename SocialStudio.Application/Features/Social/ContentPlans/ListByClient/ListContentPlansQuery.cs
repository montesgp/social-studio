using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentPlans.ListByClient;

public sealed record ListContentPlansQuery(
    Guid ClientId,
    Guid OrganizationId) : IRequest<Result<IReadOnlyList<ContentPlanSummaryResponse>>>;
