using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Niches;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Niches.Activate;

public sealed class ActivateNicheHandler
    : IRequestHandler<ActivateNicheCommand, Result<NicheResponse>>
{
    private readonly INicheRepository _niches;
    private readonly IClock _clock;

    public ActivateNicheHandler(INicheRepository niches, IClock clock)
    {
        _niches = niches;
        _clock = clock;
    }

    public async Task<Result<NicheResponse>> Handle(
        ActivateNicheCommand request,
        CancellationToken cancellationToken)
    {
        var niche = await _niches.GetByIdAsync(new NicheId(request.Id), cancellationToken);
        if (niche is null)
        {
            return Result.Failure<NicheResponse>(NicheErrors.NotFound);
        }

        var activated = niche.Activate(_clock.UtcNow);
        if (activated.IsFailure)
        {
            return Result.Failure<NicheResponse>(activated.Error);
        }

        _niches.Update(niche);
        await _niches.SaveChangesAsync(cancellationToken);

        return Result.Success(NicheResponse.FromDomain(niche));
    }
}
