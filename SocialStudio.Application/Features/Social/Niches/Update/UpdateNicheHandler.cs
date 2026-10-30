using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Niches;
using MediatR;

namespace SocialStudio.Application.Features.Social.Niches.Update;

public sealed class UpdateNicheHandler
    : IRequestHandler<UpdateNicheCommand, Result<NicheResponse>>
{
    private readonly INicheRepository _niches;
    private readonly IClock _clock;

    public UpdateNicheHandler(INicheRepository niches, IClock clock)
    {
        _niches = niches;
        _clock = clock;
    }

    public async Task<Result<NicheResponse>> Handle(
        UpdateNicheCommand request,
        CancellationToken cancellationToken)
    {
        var niche = await _niches.GetByIdAsync(new NicheId(request.Id), cancellationToken);
        if (niche is null)
        {
            return Result.Failure<NicheResponse>(NicheErrors.NotFound);
        }

        var displayName = (request.DisplayName ?? string.Empty).Trim();
        var byDisplayName = await _niches.GetByDisplayNameAsync(displayName, cancellationToken);
        if (byDisplayName is not null && byDisplayName.Id != niche.Id)
        {
            return Result.Failure<NicheResponse>(NicheErrors.DisplayNameAlreadyExists(displayName));
        }

        var updated = niche.Update(
            displayName,
            request.IconKey,
            request.SearchHashtags ?? [],
            _clock.UtcNow);

        if (updated.IsFailure)
        {
            return Result.Failure<NicheResponse>(updated.Error);
        }

        _niches.Update(niche);
        await _niches.SaveChangesAsync(cancellationToken);

        return Result.Success(NicheResponse.FromDomain(niche));
    }
}
