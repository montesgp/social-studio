using SocialStudio.Application.Abstractions.Social;
using SocialStudio.Application.Abstractions.Social.ContentDiscovery;
using SocialStudio.Application.Features.Social.ContentGeneration.Discovery;
using SocialStudio.Domain.Social.Clients;
using SocialStudio.Domain.Social.Niches;
using SocialStudio.Tests.Application.Social.Niches;
using Microsoft.Extensions.Logging;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Discovery;

/// <summary>
/// The curated hashtags of the client's assigned niches are the source of truth for niche search.
/// Deriving them from free text is the legacy fallback, kept only for clients with no assignment.
/// </summary>
public sealed class ClientNicheHashtagProviderTests
{
    private static readonly DateTime Now = new(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ClientWithOneNiche_ReportsThatNichesCuratedHashtags()
    {
        var (provider, links, niches, _) = Build();
        var clientId = ClientId.New();
        await Assign(links, niches, clientId, MakeNiche("medical-health", ["medical", "health"]));

        var resolution = await provider.GetHashtagsAsync(clientId);

        Assert.Equal(ClientNicheHashtagOutcome.Curated, resolution.Outcome);
        Assert.Equal(["medical", "health"], resolution.Hashtags);
    }

    [Fact]
    public async Task ClientWithSeveralNiches_ReportsTheDeduplicatedUnion_InAssignmentOrder()
    {
        var (provider, links, niches, _) = Build();
        var clientId = ClientId.New();
        await Assign(
            links,
            niches,
            clientId,
            MakeNiche("software-technology", ["software", "technology"]),
            // "technology" repeats on purpose: a shared hashtag must be searched once, not twice.
            MakeNiche("artificial-intelligence", ["ai", "technology"]));

        var resolution = await provider.GetHashtagsAsync(clientId);

        Assert.Equal(ClientNicheHashtagOutcome.Curated, resolution.Outcome);
        Assert.Equal(["software", "technology", "ai"], resolution.Hashtags);
    }

    [Fact]
    public async Task ClientWithNoNiches_ReportsNoNichesAssigned_SoTheCallerFallsBackToFreeText()
    {
        var (provider, _, _, _) = Build();

        var resolution = await provider.GetHashtagsAsync(ClientId.New());

        Assert.Equal(ClientNicheHashtagOutcome.NoNichesAssigned, resolution.Outcome);
        Assert.Empty(resolution.Hashtags);
    }

    [Fact]
    public async Task ClientWhoseNichesAllHaveNoCuratedHashtags_ReportsAConfigurationProblem()
    {
        var (provider, links, niches, _) = Build();
        var clientId = ClientId.New();
        await Assign(links, niches, clientId, MakeNiche("uncurated-one", []), MakeNiche("uncurated-two", []));

        var resolution = await provider.GetHashtagsAsync(clientId);

        // NOT NoNichesAssigned: falling back to the free text here would silently hide the fact that
        // the operator has to curate the catalog entry.
        Assert.Equal(ClientNicheHashtagOutcome.MissingCuratedHashtags, resolution.Outcome);
        Assert.Empty(resolution.Hashtags);
    }

    [Fact]
    public async Task TheUnion_HonoursTheHashtagCap_AndLogsThatItWasTruncated()
    {
        var (provider, links, niches, logger) = Build();
        var clientId = ClientId.New();
        await Assign(
            links,
            niches,
            clientId,
            MakeNiche("one", ["alpha", "beta"]),
            MakeNiche("two", ["gamma", "delta"]),
            MakeNiche("three", ["epsilon", "zeta"]));

        var resolution = await provider.GetHashtagsAsync(clientId);

        Assert.Equal(NicheHashtagDerivation.MaxHashtags, resolution.Hashtags.Count);
        Assert.Equal(["alpha", "beta", "gamma", "delta", "epsilon"], resolution.Hashtags);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (
        ClientNicheHashtagProvider Provider,
        InMemoryClientNicheRepository Links,
        InMemoryNicheRepository Niches,
        CapturingLogger<ClientNicheHashtagProvider> Logger) Build()
    {
        var links = new InMemoryClientNicheRepository();
        var niches = new InMemoryNicheRepository();
        var logger = new CapturingLogger<ClientNicheHashtagProvider>();

        return (new ClientNicheHashtagProvider(niches, links, logger), links, niches, logger);
    }

    private static Niche MakeNiche(string code, IReadOnlyCollection<string> hashtags) =>
        Niche.Create(code, code, null, hashtags, Now).Value;

    private static async Task Assign(
        InMemoryClientNicheRepository links,
        InMemoryNicheRepository niches,
        ClientId clientId,
        params Niche[] assigned)
    {
        foreach (var niche in assigned)
        {
            await niches.AddAsync(niche, CancellationToken.None);
        }

        await links.ReplaceAsync(clientId, assigned.Select(n => n.Id).ToList(), Now, CancellationToken.None);
    }

    /// <summary>Captures level + rendered message so a test can assert on what an operator would read.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
