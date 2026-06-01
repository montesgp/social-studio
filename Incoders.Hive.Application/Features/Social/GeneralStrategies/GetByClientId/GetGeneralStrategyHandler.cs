using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.GeneralStrategies;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.GeneralStrategies.GetByClientId;

public sealed class GetGeneralStrategyHandler
    : IRequestHandler<GetGeneralStrategyQuery, Result<GeneralStrategyResponse>>
{
    private readonly IGeneralStrategyRepository _strategies;

    public GetGeneralStrategyHandler(IGeneralStrategyRepository strategies)
    {
        _strategies = strategies;
    }

    public async Task<Result<GeneralStrategyResponse>> Handle(
        GetGeneralStrategyQuery request,
        CancellationToken cancellationToken)
    {
        var strategy = await _strategies.GetByClientIdAsync(
            request.ClientId, request.OrganizationId, cancellationToken);

        if (strategy is null)
        {
            return Result.Failure<GeneralStrategyResponse>(GeneralStrategyErrors.NotFound);
        }

        return Result.Success(GeneralStrategyResponse.FromDomain(strategy));
    }
}
