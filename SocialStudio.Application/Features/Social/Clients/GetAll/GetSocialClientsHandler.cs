using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Common.Pagination;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.Clients.GetAll;

public sealed class GetSocialClientsHandler
    : IRequestHandler<GetSocialClientsQuery, Result<PageResult<SocialClientResponse>>>
{
    private readonly ISocialClientRepository _clients;

    public GetSocialClientsHandler(ISocialClientRepository clients)
    {
        _clients = clients;
    }

    public async Task<Result<PageResult<SocialClientResponse>>> Handle(
        GetSocialClientsQuery request,
        CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _clients.ListAsync(
            new SocialClientQuery(
                request.OrganizationId,
                request.Name,
                request.IncludeInactive,
                request.Page,
                request.PageSize),
            cancellationToken);

        return Result.Success(new PageResult<SocialClientResponse>(
            items.Select(SocialClientResponse.FromDomain).ToList(),
            request.Page,
            request.PageSize,
            totalCount));
    }
}
