using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Niches;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Niches.GetById;

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
