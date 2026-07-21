using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Domain.Common;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Publications.Connections;

/// <summary>Lists a client's publish account connections. The response is token-free.</summary>
public sealed record ListClientAccountConnectionsQuery(Guid OrganizationId, Guid ClientId)
    : IRequest<Result<IReadOnlyList<ClientAccountConnectionResponse>>>;

public sealed class ListClientAccountConnectionsQueryHandler
    : IRequestHandler<ListClientAccountConnectionsQuery, Result<IReadOnlyList<ClientAccountConnectionResponse>>>
{
    private readonly IClientAccountConnectionRepository _connections;

    public ListClientAccountConnectionsQueryHandler(IClientAccountConnectionRepository connections)
    {
        _connections = connections;
    }

    public async Task<Result<IReadOnlyList<ClientAccountConnectionResponse>>> Handle(
        ListClientAccountConnectionsQuery request, CancellationToken ct)
    {
        var connections = await _connections.ListByClientAsync(request.OrganizationId, request.ClientId, ct);
        IReadOnlyList<ClientAccountConnectionResponse> response =
            connections.Select(ClientAccountConnectionResponse.FromDomain).ToList();

        return Result.Success(response);
    }
}
