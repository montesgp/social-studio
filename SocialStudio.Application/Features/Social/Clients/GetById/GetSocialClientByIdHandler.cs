using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Clients;
using MediatR;

namespace SocialStudio.Application.Features.Social.Clients.GetById;

public sealed class GetSocialClientByIdHandler
    : IRequestHandler<GetSocialClientByIdQuery, Result<SocialClientResponse>>
{
    private readonly ISocialClientRepository _clients;
    private readonly ISocialClientDirectoryRepository _directory;

    public GetSocialClientByIdHandler(
        ISocialClientRepository clients,
        ISocialClientDirectoryRepository directory)
    {
        _clients = clients;
        _directory = directory;
    }

    public async Task<Result<SocialClientResponse>> Handle(
        GetSocialClientByIdQuery request,
        CancellationToken cancellationToken)
    {
        var clientId = new ClientId(request.Id);

        // A null organization is the platform-admin lookup: the "All organizations" list can return
        // clients outside the caller's own tenant, so scoping this by tenant would 404 exactly the
        // rows that list just handed out.
        var client = request.OrganizationId is { } organizationId
            ? await _clients.GetByIdAsync(clientId, organizationId, cancellationToken)
            : await _directory.GetByIdAcrossOrganizationsAsync(clientId, cancellationToken);

        return client is null
            ? Result.Failure<SocialClientResponse>(ClientErrors.NotFound)
            : Result.Success(SocialClientResponse.FromDomain(client));
    }
}
