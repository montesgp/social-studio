using SocialStudio.Application.Abstractions;
using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Application.Features.Social.NicheDiscovery.DiscoveryRuns;
using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Tests.Application.Features.Social.NicheDiscovery.DiscoveryRuns;

/// <summary>
/// WU-11b (design §10): validation error codes plus the happy path — enqueues via
/// <see cref="INicheDiscoveryRunQueue"/> and returns <c>202 { runId, status }</c>.
/// </summary>
public sealed class StartDiscoveryRunCommandTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_HappyPath_PersistsAndEnqueuesTheRun_ReturningRequestedStatus()
    {
        var runRepo = new FakeRunRepository();
        var queue = new FakeQueue();
        var handler = BuildHandler(runRepo, queue);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Requested", result.Value.Status);
        Assert.Single(runRepo.Added);
        Assert.Equal((Org, new DiscoveryRunId(result.Value.RunId)), queue.Enqueued.Single());
    }

    [Fact]
    public async Task Handle_UnknownTargetMarket_ReturnsMarketUnknown()
    {
        var handler = BuildHandler(new FakeRunRepository(), new FakeQueue());

        var result = await handler.Handle(ValidCommand() with { TargetMarketCode = "fr-FR" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.MarketUnknown.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_WindowOutOfRange_ReturnsWindowOutOfRange()
    {
        var handler = BuildHandler(new FakeRunRepository(), new FakeQueue());

        var result = await handler.Handle(ValidCommand() with { WindowDays = 400 }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.WindowOutOfRange.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_UnknownDepth_ReturnsDepthUnknown()
    {
        var handler = BuildHandler(new FakeRunRepository(), new FakeQueue());

        var result = await handler.Handle(ValidCommand() with { Depth = "extreme" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.DepthUnknown.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_InvalidContentFormat_ReturnsContentFormatInvalid()
    {
        var handler = BuildHandler(new FakeRunRepository(), new FakeQueue());

        var result = await handler.Handle(ValidCommand() with { ContentFormat = "podcast" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.ContentFormatInvalid.Code, result.Error.Code);
    }

    [Fact]
    public async Task Handle_UnsupportedNetwork_ReturnsNetworkUnsupported()
    {
        var handler = BuildHandler(new FakeRunRepository(), new FakeQueue());

        var result = await handler.Handle(ValidCommand() with { NetworkKey = "tiktok" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(NicheDiscoveryErrors.NetworkUnsupported.Code, result.Error.Code);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static StartDiscoveryRunCommand ValidCommand() => new(
        Org, User, "es-AR", "pt-BR", "youtube", "video",
        WindowDays: null, Depth: null, NicheId: null, SeedTerms: ["budget travel"], Weights: null);

    private static StartDiscoveryRunHandler BuildHandler(FakeRunRepository runRepo, FakeQueue queue) =>
        new(runRepo, queue, new FakeMarketCatalogRepository(), new FakeClock());

    private sealed class FakeRunRepository : IDiscoveryRunRepository
    {
        public List<DiscoveryRun> Added { get; } = [];

        public Task AddAsync(DiscoveryRun run, CancellationToken ct = default)
        {
            Added.Add(run);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DiscoveryRun run, CancellationToken ct = default) => Task.CompletedTask;

        public Task<DiscoveryRun?> GetByIdAsync(Guid organizationId, DiscoveryRunId id, CancellationToken ct = default) =>
            Task.FromResult<DiscoveryRun?>(Added.FirstOrDefault(r => r.Id == id));

        public Task<IReadOnlyList<DiscoveryRun>> ListByOrganizationAsync(Guid organizationId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DiscoveryRun>>(
                Added.Where(r => r.OrganizationId == organizationId).Take(limit).ToList());
    }

    private sealed class FakeQueue : INicheDiscoveryRunQueue
    {
        public List<(Guid OrganizationId, DiscoveryRunId RunId)> Enqueued { get; } = [];

        public Task EnqueueAsync(Guid organizationId, DiscoveryRunId runId, CancellationToken ct = default)
        {
            Enqueued.Add((organizationId, runId));
            return Task.CompletedTask;
        }

        public Task<(Guid OrganizationId, DiscoveryRunId RunId)?> DequeueAsync(CancellationToken ct = default) =>
            Task.FromResult<(Guid, DiscoveryRunId)?>(null);
    }

    private sealed class FakeMarketCatalogRepository : IMarketCatalogRepository
    {
        private readonly List<MarketCatalogEntry> _entries =
        [
            MarketCatalogEntry.Create(MarketCode.TryParse("es-AR").Value, "social.niche_discovery.markets.es_ar", "es", true).Value,
            MarketCatalogEntry.Create(MarketCode.TryParse("pt-BR").Value, "social.niche_discovery.markets.pt_br", "pt", true).Value,
        ];

        public Task<IReadOnlyList<MarketCatalogEntry>> ListActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MarketCatalogEntry>>(_entries.Where(e => e.IsActive).ToList());

        public Task<MarketCatalogEntry?> GetByCodeAsync(MarketCode code, CancellationToken ct = default) =>
            Task.FromResult(_entries.FirstOrDefault(e => e.Code.Value == code.Value));
    }

    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow => Now;
    }
}
