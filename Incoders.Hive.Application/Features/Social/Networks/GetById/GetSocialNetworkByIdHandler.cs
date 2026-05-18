using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Networks;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Networks.GetById;

public sealed class GetSocialNetworkByIdHandler
    : IRequestHandler<GetSocialNetworkByIdQuery, Result<SocialNetworkResponse>>
{
    private readonly ISocialNetworkRepository _networks;

    public GetSocialNetworkByIdHandler(ISocialNetworkRepository networks)
    {
        _networks = networks;
    }

    public async Task<Result<SocialNetworkResponse>> Handle(
        GetSocialNetworkByIdQuery request,
        CancellationToken cancellationToken)
    {
        var network = await _networks.GetByIdAsync(new SocialNetworkId(request.Id), cancellationToken);
        if (network is null)
        {
            return Result.Failure<SocialNetworkResponse>(SocialNetworkErrors.NotFound);
        }

        return Result.Success(SocialNetworkResponse.FromDomain(network));
    }
}
