using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Niches;
using MediatR;

namespace SocialStudio.Application.Features.Social.Niches.GetById;

public sealed class GetNicheByIdHandler
    : IRequestHandler<GetNicheByIdQuery, Result<NicheResponse>>
{
    private readonly INicheRepository _niches;

    public GetNicheByIdHandler(INicheRepository niches)
    {
        _niches = niches;
    }

    public async Task<Result<NicheResponse>> Handle(
        GetNicheByIdQuery request,
        CancellationToken cancellationToken)
    {
        var niche = await _niches.GetByIdAsync(new NicheId(request.Id), cancellationToken);

        return niche is null
            ? Result.Failure<NicheResponse>(NicheErrors.NotFound)
            : Result.Success(NicheResponse.FromDomain(niche));
    }
}
