using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// Canned answer for the curated-hashtag port. Defaults to "this client has no niches assigned", which
/// is exactly the legacy situation: the source must keep deriving hashtags from the free-text sector.
/// </summary>
internal sealed class StubClientNicheHashtagProvider : IClientNicheHashtagProvider
{
    private readonly ClientNicheHashtags _answer;

    private StubClientNicheHashtagProvider(ClientNicheHashtags answer) => _answer = answer;

    public static StubClientNicheHashtagProvider NoNiches() =>
        new(new ClientNicheHashtags(ClientNicheHashtagOutcome.NoNichesAssigned, []));

    public static StubClientNicheHashtagProvider Curated(params string[] hashtags) =>
        new(new ClientNicheHashtags(ClientNicheHashtagOutcome.Curated, hashtags));

    public static StubClientNicheHashtagProvider MissingCuratedHashtags() =>
        new(new ClientNicheHashtags(ClientNicheHashtagOutcome.MissingCuratedHashtags, []));

    public bool WasCalled { get; private set; }

    public Task<ClientNicheHashtags> GetHashtagsAsync(ClientId clientId, CancellationToken ct = default)
    {
        WasCalled = true;
        return Task.FromResult(_answer);
    }
}
