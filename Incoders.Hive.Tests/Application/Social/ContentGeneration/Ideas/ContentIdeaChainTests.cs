using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Ideas;

namespace Incoders.Hive.Tests.Application.Social.ContentGeneration.Ideas;

/// <summary>
/// RED tests for <see cref="ContentIdeaChain"/> — the ordered chain that fills a batch with ideas.
///
/// The chain is the whole "hybrid but dynamic" rule. Nobody picks a mode: sources are asked in order
/// for whatever is still missing, and a source with nothing to give simply passes the remainder on.
/// A client with no strategy therefore ends up fully referential by itself.
/// </summary>
public sealed class ContentIdeaChainTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();

    [Fact]
    public async Task Draw_FillsFromTheFirstSourceThatCanServe()
    {
        var strategy = new FakeSource("client-strategy", 1, Ideas("client-strategy", 3));
        var market = new FakeSource("market-discovery", 3, Ideas("market-discovery", 3));
        var chain = new ContentIdeaChain([strategy, market]);

        var plan = await chain.DrawAsync(new ContentIdeaRequest(Org, Client, 3), CancellationToken.None);

        Assert.Equal(3, plan.Ideas.Count);
        Assert.All(plan.Ideas, idea => Assert.Equal("client-strategy", idea.SourceKey));
        // Nothing left to ask for, so the market is never hit — that is real money not spent.
        Assert.False(market.WasCalled);
    }

    [Fact]
    public async Task Draw_DegradesToTheNextSourceWhenOneHasNothing()
    {
        // A client with no strategy defined: the first link contributes nothing at all.
        var strategy = new FakeSource("client-strategy", 1, []);
        var market = new FakeSource("market-discovery", 3, Ideas("market-discovery", 4));
        var chain = new ContentIdeaChain([strategy, market]);

        var plan = await chain.DrawAsync(new ContentIdeaRequest(Org, Client, 4), CancellationToken.None);

        Assert.Equal(4, plan.Ideas.Count);
        Assert.All(plan.Ideas, idea => Assert.Equal("market-discovery", idea.SourceKey));
    }

    [Fact]
    public async Task Draw_AsksEachSourceOnlyForTheRemainder()
    {
        var strategy = new FakeSource("client-strategy", 1, Ideas("client-strategy", 2));
        var market = new FakeSource("market-discovery", 3, Ideas("market-discovery", 8));
        var chain = new ContentIdeaChain([strategy, market]);

        var plan = await chain.DrawAsync(new ContentIdeaRequest(Org, Client, 10), CancellationToken.None);

        Assert.Equal(10, plan.Ideas.Count);
        // The market must be asked for 8, not 10: over-fetching a scraper costs money per item.
        Assert.Equal(8, market.LastRequestedCount);
    }

    [Fact]
    public async Task Draw_RunsSourcesInOrderRegardlessOfRegistration()
    {
        var market = new FakeSource("market-discovery", 3, Ideas("market-discovery", 5));
        var strategy = new FakeSource("client-strategy", 1, Ideas("client-strategy", 5));

        // Registered market-first on purpose; DI order must not decide preference.
        var chain = new ContentIdeaChain([market, strategy]);

        var plan = await chain.DrawAsync(new ContentIdeaRequest(Org, Client, 5), CancellationToken.None);

        Assert.All(plan.Ideas, idea => Assert.Equal("client-strategy", idea.SourceKey));
    }

    [Fact]
    public async Task Draw_NeverReturnsMoreThanAsked()
    {
        var generous = new FakeSource("ai-agent", 1, Ideas("ai-agent", 50));
        var chain = new ContentIdeaChain([generous]);

        var plan = await chain.DrawAsync(new ContentIdeaRequest(Org, Client, 6), CancellationToken.None);

        // Each idea becomes a full AI + image run. An over-eager source must never inflate the bill.
        Assert.Equal(6, plan.Ideas.Count);
    }

    [Fact]
    public async Task Draw_SkipsASourceThatFailsAndKeepsGoing()
    {
        var broken = new ThrowingSource("reference-accounts", 2);
        var market = new FakeSource("market-discovery", 3, Ideas("market-discovery", 3));
        var chain = new ContentIdeaChain([broken, market]);

        var plan = await chain.DrawAsync(new ContentIdeaRequest(Org, Client, 3), CancellationToken.None);

        // A scraper outage must degrade the batch, not cancel it.
        Assert.Equal(3, plan.Ideas.Count);
        Assert.Contains("reference-accounts", plan.FailedSourceKeys);
    }

    [Fact]
    public async Task Draw_DropsDuplicateIdeasAcrossSources()
    {
        var first = new FakeSource("client-strategy", 1, [Idea("client-strategy", "Cómo elegir tu primer CRM")]);
        var second = new FakeSource("ai-agent", 2, [
            Idea("ai-agent", "cómo elegir tu primer crm"),
            Idea("ai-agent", "Errores al migrar de Excel")
        ]);
        var chain = new ContentIdeaChain([first, second]);

        var plan = await chain.DrawAsync(new ContentIdeaRequest(Org, Client, 3), CancellationToken.None);

        // Two sources landing on the same topic must not produce two near-identical pieces.
        Assert.Equal(2, plan.Ideas.Count);
    }

    [Fact]
    public async Task Draw_ReportsAShortfallWhenNoSourceCanFillTheBatch()
    {
        var strategy = new FakeSource("client-strategy", 1, Ideas("client-strategy", 2));
        var chain = new ContentIdeaChain([strategy]);

        var plan = await chain.DrawAsync(new ContentIdeaRequest(Org, Client, 10), CancellationToken.None);

        Assert.Equal(2, plan.Ideas.Count);
        Assert.Equal(8, plan.Shortfall);
    }

    [Fact]
    public async Task Draw_TouchesNoSourceWhenNothingIsAsked()
    {
        var strategy = new FakeSource("client-strategy", 1, Ideas("client-strategy", 3));
        var chain = new ContentIdeaChain([strategy]);

        var plan = await chain.DrawAsync(new ContentIdeaRequest(Org, Client, 0), CancellationToken.None);

        Assert.Empty(plan.Ideas);
        Assert.False(strategy.WasCalled);
    }

    [Fact]
    public async Task Draw_KeepsTheHighestSignalFirstWithinASource()
    {
        var market = new FakeSource("market-discovery", 1, [
            Idea("market-discovery", "tibio", signal: 1_000),
            Idea("market-discovery", "ganador", signal: 250_000),
            Idea("market-discovery", "medio", signal: 40_000)
        ]);
        var chain = new ContentIdeaChain([market]);

        var plan = await chain.DrawAsync(new ContentIdeaRequest(Org, Client, 2), CancellationToken.None);

        // When a batch is trimmed, the pieces that survive must be the winners, not the first two rows.
        Assert.Equal(["ganador", "medio"], plan.Ideas.Select(i => i.Title).ToArray());
    }

    // ── origin filter: spontaneous ideas with nothing scraped behind them ─────
    // The operator asking for "ideas that are not from anyone's winning posts" is asking to exclude
    // every OBSERVED source. That is a filter on the chain, not a new chain: the invented sources keep
    // behaving exactly as they do in a normal run.

    [Fact]
    public async Task Draw_WithInventedFilter_NeverTouchesAnObservedSource()
    {
        var invented = new FakeSource("ai-agent", 4, Ideas("ai-agent", 3), IdeaOrigin.Invented);
        var observed = new FakeSource("reference-accounts", 2, Ideas("reference-accounts", 3), IdeaOrigin.Observed);
        var chain = new ContentIdeaChain([invented, observed]);

        var plan = await chain.DrawAsync(
            new ContentIdeaRequest(Org, Client, 3, OriginFilter: IdeaOrigin.Invented),
            CancellationToken.None);

        Assert.Equal(3, plan.Ideas.Count);
        Assert.All(plan.Ideas, idea => Assert.Equal("ai-agent", idea.SourceKey));
        // The whole point of the mode: a source that scrapes must not be called AT ALL. Calling it and
        // discarding the result would still bill the run.
        Assert.False(observed.WasCalled);
    }

    [Fact]
    public async Task Draw_WithInventedFilter_ReportsAShortfallRatherThanFallingBackToScraping()
    {
        var invented = new FakeSource("ai-agent", 4, Ideas("ai-agent", 2), IdeaOrigin.Invented);
        var observed = new FakeSource("reference-accounts", 2, Ideas("reference-accounts", 8), IdeaOrigin.Observed);
        var chain = new ContentIdeaChain([invented, observed]);

        var plan = await chain.DrawAsync(
            new ContentIdeaRequest(Org, Client, 6, OriginFilter: IdeaOrigin.Invented),
            CancellationToken.None);

        // Topping the batch up from the scraper would silently give the operator exactly what they
        // asked NOT to get. A short batch with a stated shortfall is the honest outcome.
        Assert.Equal(2, plan.Ideas.Count);
        Assert.Equal(4, plan.Shortfall);
        Assert.False(observed.WasCalled);
    }

    [Fact]
    public async Task Draw_WithoutFilter_StillConsultsEverySource()
    {
        var invented = new FakeSource("ai-agent", 4, Ideas("ai-agent", 2), IdeaOrigin.Invented);
        var observed = new FakeSource("reference-accounts", 2, Ideas("reference-accounts", 5), IdeaOrigin.Observed);
        var chain = new ContentIdeaChain([invented, observed]);

        var plan = await chain.DrawAsync(new ContentIdeaRequest(Org, Client, 6), CancellationToken.None);

        // Default behaviour is unchanged: no filter means the hybrid chain everybody already relies on.
        Assert.True(observed.WasCalled);
        Assert.True(invented.WasCalled);
        Assert.Equal(6, plan.Ideas.Count);
    }

    [Fact]
    public async Task Draw_CarriesTheVariationSeedIntoEverySource()
    {
        var invented = new FakeSource("ai-agent", 4, Ideas("ai-agent", 2), IdeaOrigin.Invented);
        var chain = new ContentIdeaChain([invented]);

        await chain.DrawAsync(
            new ContentIdeaRequest(Org, Client, 2, VariationSeed: "seed-abc"),
            CancellationToken.None);

        // Without the seed reaching the source, two runs for the same client return the same topics and
        // "spontaneous ideas" becomes "the same five ideas forever".
        Assert.Equal("seed-abc", invented.LastVariationSeed);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static ContentIdea Idea(string sourceKey, string title, double? signal = null) =>
        new(title, Summary: null, SourceKey: sourceKey, Reference: null, Signal: signal);

    private static IReadOnlyList<ContentIdea> Ideas(string sourceKey, int count) =>
        Enumerable.Range(1, count).Select(i => Idea(sourceKey, $"{sourceKey}-idea-{i}")).ToList();

    private sealed class FakeSource(
        string sourceKey,
        int order,
        IReadOnlyList<ContentIdea> ideas,
        IdeaOrigin origin = IdeaOrigin.Invented)
        : IContentIdeaSource
    {
        public string SourceKey { get; } = sourceKey;
        public int Order { get; } = order;
        public IdeaOrigin Origin { get; } = origin;
        public bool WasCalled { get; private set; }
        public int LastRequestedCount { get; private set; }
        public string? LastVariationSeed { get; private set; }

        public Task<IReadOnlyList<ContentIdea>> DrawAsync(ContentIdeaRequest request, CancellationToken ct)
        {
            WasCalled = true;
            LastRequestedCount = request.Count;
            LastVariationSeed = request.VariationSeed;
            return Task.FromResult(ideas);
        }
    }

    private sealed class ThrowingSource(string sourceKey, int order) : IContentIdeaSource
    {
        public string SourceKey { get; } = sourceKey;
        public int Order { get; } = order;
        public IdeaOrigin Origin => IdeaOrigin.Observed;

        public Task<IReadOnlyList<ContentIdea>> DrawAsync(ContentIdeaRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("provider is down");
    }
}
