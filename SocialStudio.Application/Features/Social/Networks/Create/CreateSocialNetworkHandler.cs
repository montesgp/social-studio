using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Networks;
using MediatR;

namespace SocialStudio.Application.Features.Social.Networks.Create;

public sealed class CreateSocialNetworkHandler
    : IRequestHandler<CreateSocialNetworkCommand, Result<SocialNetworkResponse>>
{
    private readonly ISocialNetworkRepository _networks;
    private readonly IClock _clock;

    public CreateSocialNetworkHandler(ISocialNetworkRepository networks, IClock clock)
    {
        _networks = networks;
        _clock = clock;
    }

    public async Task<Result<SocialNetworkResponse>> Handle(
        CreateSocialNetworkCommand request,
        CancellationToken cancellationToken)
    {
        // Pre-check for duplicate code (primary path; DB constraint is defense-in-depth)
        var existing = await _networks.GetByCodeAsync(request.Code.Trim(), cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<SocialNetworkResponse>(SocialNetworkErrors.CodeAlreadyExists(request.Code));
        }

        var created = SocialNetwork.Create(request.Code, request.DisplayName, request.IconKey, _clock.UtcNow);
        if (created.IsFailure)
        {
            return Result.Failure<SocialNetworkResponse>(created.Error);
        }

        await _networks.AddAsync(created.Value, cancellationToken);
        await _networks.SaveChangesAsync(cancellationToken);

        return Result.Success(SocialNetworkResponse.FromDomain(created.Value));
    }
}
