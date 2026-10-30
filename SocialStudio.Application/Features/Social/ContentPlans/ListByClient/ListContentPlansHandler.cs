using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.ContentPlans.ListByClient;

public sealed class ListContentPlansHandler
    : IRequestHandler<ListContentPlansQuery, Result<IReadOnlyList<ContentPlanSummaryResponse>>>
{
    private readonly IContentPlanRepository _contentPlans;

    public ListContentPlansHandler(IContentPlanRepository contentPlans)
    {
        _contentPlans = contentPlans;
    }

    public async Task<Result<IReadOnlyList<ContentPlanSummaryResponse>>> Handle(
        ListContentPlansQuery request,
        CancellationToken cancellationToken)
    {
        var plans = await _contentPlans.ListByClientAsync(
            request.OrganizationId, request.ClientId, cancellationToken);

        var summaries = plans
            .Select(ContentPlanSummaryResponse.FromDomain)
            .ToList();

        return Result.Success<IReadOnlyList<ContentPlanSummaryResponse>>(summaries);
    }
}
