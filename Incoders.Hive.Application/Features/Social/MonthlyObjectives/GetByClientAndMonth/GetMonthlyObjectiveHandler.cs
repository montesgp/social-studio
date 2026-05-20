using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.MonthlyObjectives;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.MonthlyObjectives.GetByClientAndMonth;

public sealed class GetMonthlyObjectiveHandler
    : IRequestHandler<GetMonthlyObjectiveQuery, Result<MonthlyObjectiveResponse>>
{
    private readonly IMonthlyObjectiveRepository _objectives;

    public GetMonthlyObjectiveHandler(IMonthlyObjectiveRepository objectives)
    {
        _objectives = objectives;
    }

    public async Task<Result<MonthlyObjectiveResponse>> Handle(
        GetMonthlyObjectiveQuery request,
        CancellationToken cancellationToken)
    {
        var objective = await _objectives.GetByClientAndMonthAsync(
            request.ClientId, request.OrganizationId, request.Year, request.Month, cancellationToken);

        if (objective is null)
        {
            return Result.Failure<MonthlyObjectiveResponse>(MonthlyObjectiveErrors.NotFound);
        }

        return Result.Success(MonthlyObjectiveResponse.FromDomain(objective));
    }
}
