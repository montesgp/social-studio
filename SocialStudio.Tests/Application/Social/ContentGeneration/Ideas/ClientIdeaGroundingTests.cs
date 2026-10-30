using SocialStudio.Application.Abstractions.Social.ContentGeneration;
using SocialStudio.Application.Features.Social.ContentGeneration.Ideas;
using SocialStudio.Domain.Social.ContentGeneration;

namespace SocialStudio.Tests.Application.Social.ContentGeneration.Ideas;

/// <summary>
/// Tests for the two groundings that feed <see cref="AiClientIdeaSource"/>.
///
/// The strategy grounding is DELIBERATELY strict: it grounds only on authored strategy, so a client
/// who never wrote one contributes nothing and the chain degrades to the next source. The client
/// knowledge grounding is deliberately permissive: it stands on whatever the client record holds, and
/// only gives up when we know effectively nothing.
/// </summary>
public sealed class ClientIdeaGroundingTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();

    // ── strategy grounding ───────────────────────────────────────────────────

    [Fact]
    public async Task Strategy_GroundsOnAuthoredStrategyText()
    {
        var grounding = new ClientStrategyIdeaGrounding(
            Loader(Snapshot(contentStrategy: new ContentStrategySnapshot("Educar sobre migración a CRM", 1, 2))));

        var result = await grounding.BuildAsync(Org, Client, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Educar sobre migración a CRM", result);
    }

    [Fact]
    public async Task Strategy_ReturnsNullWhenNoStrategyWasAuthored()
    {
        var grounding = new ClientStrategyIdeaGrounding(Loader(Snapshot()));

        // THE defining degradation case: no strategy => the hybrid plan becomes referential.
        Assert.Null(await grounding.BuildAsync(Org, Client, CancellationToken.None));
    }

    [Fact]
    public async Task Strategy_ReturnsNullWhenStrategyTextIsBlank()
    {
        var grounding = new ClientStrategyIdeaGrounding(
            Loader(Snapshot(contentStrategy: new ContentStrategySnapshot("   ", 1, 2))));

        Assert.Null(await grounding.BuildAsync(Org, Client, CancellationToken.None));
    }

    [Fact]
    public async Task Strategy_GroundsOnPillarsAndObjectivesWhenPresent()
    {
        var grounding = new ClientStrategyIdeaGrounding(Loader(Snapshot(
            contentStrategy: new ContentStrategySnapshot("Plan trimestral", 1, 2),
            strategy: new StrategySnapshot(["Educación", "Casos de éxito"], "PyMEs argentinas", "3x semana"),
            objective: new ClientMonthlyObjectiveSnapshot("Generar leads", ["MQLs", "CTR"]))));

        var result = await grounding.BuildAsync(Org, Client, CancellationToken.None);

        Assert.Contains("Educación", result);
        Assert.Contains("PyMEs argentinas", result);
        Assert.Contains("Generar leads", result);
    }

    [Fact]
    public async Task Strategy_StillGroundsWhenOnlyPillarsExist()
    {
        // Pillars are authored intent too — refusing to use them would waste real strategy.
        var grounding = new ClientStrategyIdeaGrounding(Loader(Snapshot(
            strategy: new StrategySnapshot(["Educación"], null, null))));

        var result = await grounding.BuildAsync(Org, Client, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Educación", result);
    }

    [Fact]
    public async Task Strategy_IgnoresEmptyPillarList()
    {
        var grounding = new ClientStrategyIdeaGrounding(Loader(Snapshot(
            strategy: new StrategySnapshot([], null, null))));

        Assert.Null(await grounding.BuildAsync(Org, Client, CancellationToken.None));
    }

    // ── client knowledge grounding ───────────────────────────────────────────

    [Fact]
    public async Task Knowledge_GroundsOnTheClientProfile()
    {
        var grounding = new ClientKnowledgeIdeaGrounding(Loader(Snapshot(
            client: new ClientProfileSnapshot("Incoders", "Software a medida", "Tecnología"))));

        var result = await grounding.BuildAsync(Org, Client, CancellationToken.None);

        Assert.Contains("Incoders", result);
        Assert.Contains("Tecnología", result);
    }

    [Fact]
    public async Task Knowledge_IncludesVoiceAndReferentAccounts()
    {
        var grounding = new ClientKnowledgeIdeaGrounding(Loader(Snapshot(
            client: new ClientProfileSnapshot("Incoders", null, "Tecnología"),
            brand: new BrandManualSnapshot([], [], [], [], "Cercano y directo", null),
            referents: [new ClientAccountSnapshot("instagram", "Instagram", "https://ig.com/ref", "@ref", null, null)])));

        var result = await grounding.BuildAsync(Org, Client, CancellationToken.None);

        Assert.Contains("Cercano y directo", result);
        Assert.Contains("@ref", result);
    }

    [Fact]
    public async Task Knowledge_ReturnsNullWhenNothingIsKnown()
    {
        // Asking a model to invent topics for a client we know nothing about burns tokens to produce
        // generic filler. Better to contribute nothing and let the market source speak.
        var grounding = new ClientKnowledgeIdeaGrounding(Loader(BrandSnapshot.Empty));

        Assert.Null(await grounding.BuildAsync(Org, Client, CancellationToken.None));
    }

    [Fact]
    public async Task Knowledge_GroundsEvenWithoutAnAuthoredStrategy()
    {
        // This is what separates it from the strategy grounding: a name and a niche are enough.
        var grounding = new ClientKnowledgeIdeaGrounding(Loader(Snapshot(
            client: new ClientProfileSnapshot("Incoders", null, "Tecnología"))));

        Assert.NotNull(await grounding.BuildAsync(Org, Client, CancellationToken.None));
    }

    [Fact]
    public async Task Groundings_DeclareDistinctKeys()
    {
        var strategy = new ClientStrategyIdeaGrounding(Loader(BrandSnapshot.Empty));
        var knowledge = new ClientKnowledgeIdeaGrounding(Loader(BrandSnapshot.Empty));

        Assert.NotEqual(strategy.GroundingKey, knowledge.GroundingKey);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static BrandSnapshot Snapshot(
        ClientProfileSnapshot? client = null,
        BrandManualSnapshot? brand = null,
        StrategySnapshot? strategy = null,
        ClientMonthlyObjectiveSnapshot? objective = null,
        ContentStrategySnapshot? contentStrategy = null,
        IReadOnlyList<ClientAccountSnapshot>? referents = null) =>
        BrandSnapshot.FromBrandData(
            brand, strategy, objective, client, referents, null, contentStrategy);

    private static IClientBrandSnapshotLoader Loader(BrandSnapshot snapshot) => new FakeLoader(snapshot);

    private sealed class FakeLoader(BrandSnapshot snapshot) : IClientBrandSnapshotLoader
    {
        public Task<BrandSnapshot> LoadAsync(Guid organizationId, Guid clientId, CancellationToken ct = default) =>
            Task.FromResult(snapshot);
    }
}
