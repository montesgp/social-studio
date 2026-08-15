using Incoders.Hive.Application.Abstractions.Social.NicheDiscovery;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.NicheDiscovery;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>Poll endpoint (design §10): the run's current lifecycle status and execution outcome.</summary>
public sealed record GetDiscoveryRunQuery(Guid OrganizationId, Guid RunId) : IRequest<Result<DiscoveryRunStatusResponse>>;

public sealed class GetDiscoveryRunHandler : IRequestHandler<GetDiscoveryRunQuery, Result<DiscoveryRunStatusResponse>>
{
    private readonly IDiscoveryRunRepository _runRepository;

    public GetDiscoveryRunHandler(IDiscoveryRunRepository runRepository)
    {
        _runRepository = runRepository;
    }

    public async Task<Result<DiscoveryRunStatusResponse>> Handle(GetDiscoveryRunQuery request, CancellationToken cancellationToken)
    {
        var run = await _runRepository.GetByIdAsync(request.OrganizationId, new DiscoveryRunId(request.RunId), cancellationToken);
        if (run is null)
        {
            return Result.Failure<DiscoveryRunStatusResponse>(NicheDiscoveryErrors.RunNotFound);
        }

        return Result.Success(DiscoveryRunStatusResponse.FromRun(run));
    }
}
