using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Networks;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Networks.Activate;

public sealed class ActivateSocialNetworkHandler
    : IRequestHandler<ActivateSocialNetworkCommand, Result<SocialNetworkResponse>>
{
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public ActivateSocialNetworkHandler(ISocialNetworkRepository networks, IClock clock)
    {
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<SocialNetworkResponse>> Handle(
        ActivateSocialNetworkCommand request,
        CancellationToken cancellationToken)
    {
        var network = await _networks.GetByIdAsync(new SocialNetworkId(request.Id), cancellationToken);
        if (network is null)
        {
            return Result.Failure<SocialNetworkResponse>(SocialNetworkErrors.NotFound);
        }

        var activated = network.Activate(_clock.UtcNow);
        if (activated.IsFailure)
        {
            return Result.Failure<SocialNetworkResponse>(activated.Error);
        }

        _networks.Update(network);
        await _networks.SaveChangesAsync(cancellationToken);

        return Result.Success(SocialNetworkResponse.FromDomain(network));
    }
}
