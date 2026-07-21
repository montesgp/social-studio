using Incoders.Hive.Domain.Social.ClientAccountConnections;
using Incoders.Hive.Domain.Social.Networks;

namespace Incoders.Hive.Application.Abstractions.Social.Publications;

/// <summary>
/// Persistence port for <see cref="ClientAccountConnection"/> aggregates. All access is
/// organization-scoped.
/// </summary>
public interface IClientAccountConnectionRepository
{
    Task<ClientAccountConnection?> GetByIdAsync(
        Guid organizationId, ClientAccountConnectionId id, CancellationToken ct);

    Task<IReadOnlyList<ClientAccountConnection>> ListByClientAsync(
        Guid organizationId, Guid clientId, CancellationToken ct);

    /// <summary>The client's single connection for a given network (v1: one per (client, network)).</summary>
    Task<ClientAccountConnection?> GetByClientNetworkAsync(
        Guid organizationId, Guid clientId, SocialNetworkId socialNetworkId, CancellationToken ct);

    Task AddAsync(ClientAccountConnection connection, CancellationToken ct);

    Task UpdateAsync(ClientAccountConnection connection, CancellationToken ct);
}
