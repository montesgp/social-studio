using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.Publications;
using SocialStudio.Domain.Common;
using MediatR;

namespace SocialStudio.Application.Features.Social.Publications.Connections;

/// <summary>Lists a client's publish account connections. The response is token-free.</summary>
public sealed record ListClientAccountConnectionsQuery(Guid OrganizationId, Guid ClientId)
    : IRequest<Result<IReadOnlyList<ClientAccountConnectionResponse>>>;

public sealed class ListClientAccountConnectionsQueryHandler
    : IRequestHandler<ListClientAccountConnectionsQuery, Result<IReadOnlyList<ClientAccountConnectionResponse>>>
{
    private readonly IClientAccountConnectionRepository _connections;
    private readonly ISocialNetworkRepository _networks;

    public ListClientAccountConnectionsQueryHandler(
        IClientAccountConnectionRepository connections,
        ISocialNetworkRepository networks)
    {
        _connections = connections;
        _networks = networks;
    }

    public async Task<Result<IReadOnlyList<ClientAccountConnectionResponse>>> Handle(
        ListClientAccountConnectionsQuery request, CancellationToken ct)
    {
        var connections = await _connections.ListByClientAsync(request.OrganizationId, request.ClientId, ct);

        // Batch-load the referenced networks so the projection can carry the network CODE (the value
        // publish commands need) instead of only an opaque id. GetByIdsAsync exists precisely to keep
        // this off the N+1 path.
        var networkIds = connections.Select(c => c.SocialNetworkId).Distinct().ToList();
        var networksById = networkIds.Count == 0
            ? []
            : (await _networks.GetByIdsAsync(networkIds, ct)).ToDictionary(n => n.Id);

        IReadOnlyList<ClientAccountConnectionResponse> response = connections
            .Select(c => ClientAccountConnectionResponse.FromDomain(
                c, networksById.GetValueOrDefault(c.SocialNetworkId)))
            .ToList();

        return Result.Success(response);
    }
}
