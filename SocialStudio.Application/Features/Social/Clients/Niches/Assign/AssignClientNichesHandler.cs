using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Features.Social.Niches;
using SocialStudio.Domain.Common;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Niches;
using MediatR;

namespace SocialStudio.Application.Features.Social.Clients.Niches.Assign;

/// <summary>
/// Replaces a client's niche selection with entries picked from the sysadmin-managed catalog.
/// Unknown or deactivated ids are rejected rather than created on the fly — that is the whole point
/// of having a catalog instead of a free-text field.
/// </summary>
public sealed class AssignClientNichesHandler
    : IRequestHandler<AssignClientNichesCommand, Result<ClientNichesResponse>>
{
    private readonly ISocialClientRepository _clients;
    private readonly ISocialClientDirectoryRepository _directory;
    private readonly INicheRepository _niches;
    private readonly IClientNicheRepository _links;
    private readonly IClock _clock;

    public AssignClientNichesHandler(
        ISocialClientRepository clients,
        ISocialClientDirectoryRepository directory,
        INicheRepository niches,
        IClientNicheRepository links,
        IClock clock)
    {
        _clients = clients;
        _directory = directory;
        _niches = niches;
        _links = links;
        _clock = clock;
    }

    public async Task<Result<ClientNichesResponse>> Handle(
        AssignClientNichesCommand request,
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

        var requestedIds = (request.NicheIds ?? [])
            .Distinct()
            .Select(id => new NicheId(id))
            .ToList();

        IReadOnlyList<Niche> resolved = requestedIds.Count == 0
            ? []
            : await _niches.GetByIdsAsync(requestedIds, cancellationToken);

        if (resolved.Count != requestedIds.Count)
        {
            return Result.Failure<ClientNichesResponse>(NicheErrors.NotFound);
        }

        if (resolved.Any(x => !x.IsActive))
        {
            return Result.Failure<ClientNichesResponse>(NicheErrors.Inactive);
        }

        await _links.ReplaceAsync(clientId, requestedIds, _clock.UtcNow, cancellationToken);
        await _links.SaveChangesAsync(cancellationToken);

        return Result.Success(ClientNicheMapper.ToResponse(clientId, resolved));
    }
}
