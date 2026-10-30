using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.Clients.Niches.Get;

public sealed class GetClientNichesHandler
    : IRequestHandler<GetClientNichesQuery, Result<ClientNichesResponse>>
{
    private readonly ISocialClientRepository _clients;
    private readonly ISocialClientDirectoryRepository _directory;
    private readonly INicheRepository _niches;
    private readonly IClientNicheRepository _links;

    public GetClientNichesHandler(
        ISocialClientRepository clients,
        ISocialClientDirectoryRepository directory,
        INicheRepository niches,
        IClientNicheRepository links)
    {
        _clients = clients;
        _directory = directory;
        _niches = niches;
        _links = links;
    }

    public async Task<Result<ClientNichesResponse>> Handle(
        GetClientNichesQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.ClientId);

        var client = request.OrganizationId is { } organizationId
            ? await _clients.GetByIdAsync(clientId, organizationId, cancellationToken)
            : await _directory.GetByIdAcrossOrganizationsAsync(clientId, cancellationToken);

        if (client is null)
        {
            return Result.Failure<ClientNichesResponse>(ClientErrors.NotFound);
        }

        var nicheIds = await _links.GetNicheIdsAsync(clientId, cancellationToken);

        // A niche deactivated after assignment still belongs to the selection: hiding it would make
        // the form silently drop data the operator never touched.
        IReadOnlyList<Domain.Social.Niches.Niche> niches = nicheIds.Count == 0
            ? []
            : await _niches.GetByIdsAsync(nicheIds, cancellationToken);

        return Result.Success(ClientNicheMapper.ToResponse(clientId, niches));
    }
}
