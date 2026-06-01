using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ContentPlans;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ContentPlans.GetByPeriod;

public sealed class GetContentPlanHandler
    : IRequestHandler<GetContentPlanQuery, Result<ContentPlanResponse>>
{
    private readonly IContentPlanRepository _contentPlans;

    public GetContentPlanHandler(IContentPlanRepository contentPlans)
    {
        _contentPlans = contentPlans;
    }

    public async Task<Result<ContentPlanResponse>> Handle(
        GetContentPlanQuery request,
        CancellationToken cancellationToken)
    {
        var periodResult = Period.Monthly(request.Year, request.Month);
        if (periodResult.IsFailure)
        {
            return Result.Failure<ContentPlanResponse>(periodResult.Error);
        }

        var plan = await _contentPlans.GetByClientAndPeriodAsync(
            request.OrganizationId, request.ClientId, periodResult.Value, cancellationToken);

        if (plan is null)
        {
            return Result.Failure<ContentPlanResponse>(ContentPlanErrors.NotFound);
        }

        return Result.Success(ContentPlanResponse.FromDomain(plan));
    }
}
