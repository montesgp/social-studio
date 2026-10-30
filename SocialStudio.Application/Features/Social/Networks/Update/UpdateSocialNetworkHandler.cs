using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Networks;
using MediatR;

namespace SocialStudio.Application.Features.Social.Networks.Update;

public sealed class UpdateSocialNetworkHandler
    : IRequestHandler<UpdateSocialNetworkCommand, Result<SocialNetworkResponse>>
{
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public UpdateSocialNetworkHandler(ISocialNetworkRepository networks, IClock clock)
    {
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<SocialNetworkResponse>> Handle(
        UpdateSocialNetworkCommand request,
        CancellationToken cancellationToken)
    {
        var network = await _networks.GetByIdAsync(new SocialNetworkId(request.Id), cancellationToken);
        if (network is null)
        {
            return Result.Failure<SocialNetworkResponse>(SocialNetworkErrors.NotFound);
        }

        var renamed = network.Rename(request.DisplayName, request.IconKey, _clock.UtcNow);
        if (renamed.IsFailure)
        {
            return Result.Failure<SocialNetworkResponse>(renamed.Error);
        }

        _networks.Update(network);
        await _networks.SaveChangesAsync(cancellationToken);

        return Result.Success(SocialNetworkResponse.FromDomain(network));
    }
}
