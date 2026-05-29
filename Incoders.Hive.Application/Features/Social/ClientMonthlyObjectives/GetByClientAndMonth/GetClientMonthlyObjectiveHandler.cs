using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientMonthlyObjectives;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.GetByClientAndMonth;

public sealed class GetClientMonthlyObjectiveHandler
    : IRequestHandler<GetClientMonthlyObjectiveQuery, Result<ClientMonthlyObjectiveResponse>>
{
    private readonly IClientMonthlyObjectiveRepository _objectives;

    public GetClientMonthlyObjectiveHandler(IClientMonthlyObjectiveRepository objectives)
    {
        _objectives = objectives;
    }

    public async Task<Result<ClientMonthlyObjectiveResponse>> Handle(
        GetClientMonthlyObjectiveQuery request,
        CancellationToken cancellationToken)
    {
        var objective = await _objectives.GetByClientAndMonthAsync(
            request.ClientId, request.OrganizationId, request.Year, request.Month, cancellationToken);

        if (objective is null)
        {
            return Result.Failure<ClientMonthlyObjectiveResponse>(ClientMonthlyObjectiveErrors.NotFound);
        }

        return Result.Success(ClientMonthlyObjectiveResponse.FromDomain(objective));
    }
}
