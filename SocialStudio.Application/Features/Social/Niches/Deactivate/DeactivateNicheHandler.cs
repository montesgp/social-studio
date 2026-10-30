using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Niches;
using MediatR;

namespace SocialStudio.Application.Features.Social.Niches.Deactivate;

public sealed class DeactivateNicheHandler
    : IRequestHandler<DeactivateNicheCommand, Result<NicheResponse>>
{
    private readonly INicheRepository _niches;
    private readonly IClock _clock;

    public DeactivateNicheHandler(INicheRepository niches, IClock clock)
    {
        _niches = niches;
        _clock = clock;
    }

    public async Task<Result<NicheResponse>> Handle(
        DeactivateNicheCommand request,
        CancellationToken cancellationToken)
    {
        var niche = await _niches.GetByIdAsync(new NicheId(request.Id), cancellationToken);
        if (niche is null)
        {
            return Result.Failure<NicheResponse>(NicheErrors.NotFound);
        }

        var deactivated = niche.Deactivate(_clock.UtcNow);
        if (deactivated.IsFailure)
        {
            return Result.Failure<NicheResponse>(deactivated.Error);
        }

        _niches.Update(niche);
        await _niches.SaveChangesAsync(cancellationToken);

        return Result.Success(NicheResponse.FromDomain(niche));
    }
}
