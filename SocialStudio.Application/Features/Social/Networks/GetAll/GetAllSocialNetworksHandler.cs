using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Common.Pagination;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Networks.GetAll;

public sealed class GetAllSocialNetworksHandler
    : IRequestHandler<GetAllSocialNetworksQuery, Result<PageResult<SocialNetworkResponse>>>
{
    private readonly ISocialNetworkRepository _networks;

    public GetAllSocialNetworksHandler(ISocialNetworkRepository networks)
    {
        _networks = networks;
    }

    public async Task<Result<PageResult<SocialNetworkResponse>>> Handle(
        GetAllSocialNetworksQuery request,
        CancellationToken cancellationToken)
    {
        var pagedResult = await _networks.ListAsync(
            new SocialNetworkListQuery(
                request.IncludeInactive,
                request.Page,
                request.PageSize,
                request.SearchTerm),
            cancellationToken);

        var mapped = new PageResult<SocialNetworkResponse>(
            pagedResult.Items.Select(SocialNetworkResponse.FromDomain).ToList(),
            pagedResult.Page,
            pagedResult.PageSize,
            pagedResult.TotalCount);

        return Result.Success(mapped);
    }
}
