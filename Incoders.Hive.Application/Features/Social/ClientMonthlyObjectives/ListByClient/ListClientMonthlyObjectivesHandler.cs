using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.GetByClientAndMonth;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientMonthlyObjectives.ListByClient;

public sealed class ListClientMonthlyObjectivesHandler
    : IRequestHandler<ListClientMonthlyObjectivesQuery, Result<ClientMonthlyObjectivesListResponse>>
{
    private readonly IClientMonthlyObjectiveRepository _objectives;

    public ListClientMonthlyObjectivesHandler(IClientMonthlyObjectiveRepository objectives)
    {
        _objectives = objectives;
    }

    public async Task<Result<ClientMonthlyObjectivesListResponse>> Handle(
        ListClientMonthlyObjectivesQuery request,
        CancellationToken cancellationToken)
    {
        var objectives = await _objectives.ListByClientAsync(
            request.ClientId, request.OrganizationId, cancellationToken);

        var items = objectives.Select(ClientMonthlyObjectiveResponse.FromDomain).ToList();
        return Result.Success(new ClientMonthlyObjectivesListResponse(items));
    }
}
