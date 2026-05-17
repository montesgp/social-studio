using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Common.Pagination;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Clients.GetAll;

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
        if (request.OrganizationId == Guid.Empty)
        {
            return Result.Failure<PageResult<SocialClientResponse>>(ClientErrors.OrganizationRequired);
        }

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
            totalCount,
            request.Page,
            request.PageSize));
    }
}
