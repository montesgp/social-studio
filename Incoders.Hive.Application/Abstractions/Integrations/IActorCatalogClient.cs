using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Application.Abstractions.Integrations;

/// <summary>
/// Provider anti-corruption port for listing the scraping actors available to a concrete credential.
/// Implementations live in Infrastructure and map provider-specific DTOs into this stable shape.
///
/// <para>
/// The result is a CATALOG that feeds the connector picker. It is never a routing table: which
/// network/variant an actor serves, and whether it is primary or fallback, stays organization-owned
/// configuration.
/// </para>
/// </summary>
public interface IActorCatalogClient
{
    Task<Result<IReadOnlyList<ProviderActor>>> ListActorsAsync(
        CancellationToken cancellationToken = default);
}

/// <param name="ActorId">Provider-canonical identifier of the actor.</param>
/// <param name="Name">Technical actor name, unique within <paramref name="Username"/>.</param>
/// <param name="Username">Account that owns/publishes the actor.</param>
/// <param name="DisplayName">Human-friendly label, already resolved by the adapter.</param>
/// <param name="IsOwned">
/// True when the actor belongs to the connected account itself, false when it is a public-store
/// actor the account merely uses. Both kinds are routable.
/// </param>
public sealed record ProviderActor(
    string ActorId,
    string Name,
    string Username,
    string DisplayName,
    bool IsOwned,
    IReadOnlyDictionary<string, string> Metadata);
