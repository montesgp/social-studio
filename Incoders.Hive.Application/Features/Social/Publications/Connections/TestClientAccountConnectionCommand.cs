using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social.Publications;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.ClientAccountConnections;
using Incoders.Hive.Domain.Social.Publications;
using MediatR;

namespace Incoders.Hive.Application.Features.Social.Publications.Connections;

/// <summary>
/// Tests a <see cref="ClientAccountConnection"/> by exercising the same two-level credential
/// resolution (design §6.2) the publish flow uses: it succeeds only when BOTH the per-client
/// connection and the org-level app access resolve cleanly. Records the outcome via
/// <see cref="ClientAccountConnection.MarkTestResult"/>. This reuses
/// <see cref="IPublicationDestinationResolver"/> rather than adding a separate provider "verify"
/// call to the connector port — a resolvable destination is the meaningful precondition for
/// publishing, and the resolver already covers both credential levels.
/// </summary>
public sealed record TestClientAccountConnectionCommand(Guid OrganizationId, Guid ConnectionId)
    : IRequest<Result<ClientAccountConnectionResponse>>;

public sealed class TestClientAccountConnectionCommandHandler
    : IRequestHandler<TestClientAccountConnectionCommand, Result<ClientAccountConnectionResponse>>
{
    private readonly IClientAccountConnectionRepository _connections;
    private readonly IPublicationDestinationResolver _destinationResolver;
    private readonly IClock _clock;

    public TestClientAccountConnectionCommandHandler(
        IClientAccountConnectionRepository connections,
        IPublicationDestinationResolver destinationResolver,
        IClock clock)
    {
        _connections = connections;
        _destinationResolver = destinationResolver;
        _clock = clock;
    }

    public async Task<Result<ClientAccountConnectionResponse>> Handle(
        TestClientAccountConnectionCommand request, CancellationToken ct)
    {
        var connection = await _connections.GetByIdAsync(
            request.OrganizationId, new ClientAccountConnectionId(request.ConnectionId), ct);

        if (connection is null)
        {
            return Result.Failure<ClientAccountConnectionResponse>(
                ClientAccountConnectionErrors.Validation("Connection not found."));
        }

        // v1 supports Instagram only; ClientAccountConnection does not persist a network key
        // redundantly (it already carries SocialNetworkId).
        var destination = new PublicationDestination(connection.Id.Value, connection.SocialNetworkId.Value, "instagram");

        var resolved = await _destinationResolver.ResolveAsync(request.OrganizationId, destination, ct);

        connection.MarkTestResult(
            resolved.IsSuccess, resolved.IsSuccess ? null : resolved.Error.MessageKey, _clock.UtcNow);

        await _connections.UpdateAsync(connection, ct);
        return Result.Success(ClientAccountConnectionResponse.FromDomain(connection));
    }
}
