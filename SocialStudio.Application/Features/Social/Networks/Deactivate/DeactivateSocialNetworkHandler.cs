using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Networks;
using MediatR;

namespace SocialStudio.Application.Features.Social.Networks.Deactivate;

public sealed class DeactivateSocialNetworkHandler
    : IRequestHandler<DeactivateSocialNetworkCommand, Result<SocialNetworkResponse>>
{
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public DeactivateSocialNetworkHandler(ISocialNetworkRepository networks, IClock clock)
    {
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<SocialNetworkResponse>> Handle(
        DeactivateSocialNetworkCommand request,
        CancellationToken cancellationToken)
    {
        var network = await _networks.GetByIdAsync(new SocialNetworkId(request.Id), cancellationToken);
        if (network is null)
        {
            return Result.Failure<SocialNetworkResponse>(SocialNetworkErrors.NotFound);
        }

        var deactivated = network.Deactivate(_clock.UtcNow);
        if (deactivated.IsFailure)
        {
            return Result.Failure<SocialNetworkResponse>(deactivated.Error);
        }

        _networks.Update(network);
        await _networks.SaveChangesAsync(cancellationToken);

        return Result.Success(SocialNetworkResponse.FromDomain(network));
    }
}
