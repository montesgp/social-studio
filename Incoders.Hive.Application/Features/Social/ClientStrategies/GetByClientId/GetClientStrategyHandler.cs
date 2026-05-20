using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientStrategies;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.ClientStrategies.GetByClientId;

public sealed class GetClientStrategyHandler
    : IRequestHandler<GetClientStrategyQuery, Result<ClientStrategyResponse>>
{
    private readonly IClientStrategyRepository _strategies;

    public GetClientStrategyHandler(IClientStrategyRepository strategies)
    {
        _strategies = strategies;
    }

    public async Task<Result<ClientStrategyResponse>> Handle(
        GetClientStrategyQuery request,
        CancellationToken cancellationToken)
    {
        var strategy = await _strategies.GetByClientIdAsync(
            request.ClientId, request.OrganizationId, cancellationToken);

        if (strategy is null)
        {
            return Result.Failure<ClientStrategyResponse>(ClientStrategyErrors.NotFound);
        }

        return Result.Success(ClientStrategyResponse.FromDomain(strategy));
    }
}
