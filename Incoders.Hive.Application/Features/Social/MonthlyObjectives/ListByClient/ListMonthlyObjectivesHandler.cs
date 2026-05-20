using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.MonthlyObjectives.GetByClientAndMonth;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.MonthlyObjectives.ListByClient;

public sealed class ListMonthlyObjectivesHandler
    : IRequestHandler<ListMonthlyObjectivesQuery, Result<MonthlyObjectivesListResponse>>
{
    private readonly IMonthlyObjectiveRepository _objectives;

    public ListMonthlyObjectivesHandler(IMonthlyObjectiveRepository objectives)
    {
        _objectives = objectives;
    }

    public async Task<Result<MonthlyObjectivesListResponse>> Handle(
        ListMonthlyObjectivesQuery request,
        CancellationToken cancellationToken)
    {
        var objectives = await _objectives.ListByClientAsync(
            request.ClientId, request.OrganizationId, cancellationToken);

        var items = objectives.Select(MonthlyObjectiveResponse.FromDomain).ToList();
        return Result.Success(new MonthlyObjectivesListResponse(items));
    }
}
