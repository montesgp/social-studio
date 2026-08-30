using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentPlans.ListByClient;

public sealed record ListContentPlansQuery(
    Guid ClientId,
    Guid OrganizationId) : IRequest<Result<IReadOnlyList<ContentPlanSummaryResponse>>>;
