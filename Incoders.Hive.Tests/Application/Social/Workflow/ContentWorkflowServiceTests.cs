using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Features.Social.Workflow;
using Incoders.Hive.Infrastructure.Social.Workflow;
using Incoders.Hive.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.Workflow;

public sealed class ContentWorkflowServiceTests
{
    [Fact]
    public async Task Start_ThenGetResults_ReturnsPaginatedEnvelope()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = CreateService(clock, ScopeFactoryWith(Discovered(30)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(
                Guid.NewGuid(),
                "CM Team",
                "viral",
                "mixed",
                ["tiktok", "instagram"]));

        Assert.True(started.IsSuccess);
        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var page1 = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);
        var page2 = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 2, 10);

        Assert.True(page1.IsSuccess);
        Assert.True(page2.IsSuccess);
        Assert.Equal(10, page1.Value.Items.Count);
        Assert.Equal(10, page2.Value.Items.Count);
        Assert.Equal(30, page1.Value.TotalCount);
        Assert.Equal(1, page1.Value.Page);
        Assert.Equal(2, page2.Value.Page);
        Assert.NotEqual(page1.Value.Items[0].Id, page2.Value.Items[0].Id);
    }

    [Fact]
    public async Task Select_Action_AndPackage_WorksForChosenItems()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = CreateService(clock, ScopeFactoryWith(Discovered(30)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(
                Guid.NewGuid(),
                "Acme Social",
                "niche",
                "educational",
                ["tiktok"]));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 5);
        var selectedIds = results.Value.Items.Take(2).Select(x => x.Id).ToList();

        var selection = await service.SetSelectionAsync(organizationId, started.Value.WorkflowId, selectedIds);
        var action = await service.RunActionAsync(
            organizationId,
            started.Value.WorkflowId,
            "download_audio",
            selectedIds);
        var package = await service.GeneratePackageAsync(organizationId, started.Value.WorkflowId);

        Assert.True(selection.IsSuccess);
        Assert.Equal(2, selection.Value.SelectedCount);
        Assert.True(action.IsSuccess);
        Assert.Equal("completed", action.Value.Status);
        Assert.True(package.IsSuccess);
        // Two selected proposals x the default two formats = four queued pieces.
        Assert.Equal(4, package.Value.ItemCount);
        // Queued, not ready: the pieces drain through the worker into the content bank.
        Assert.Equal("generation_queued", package.Value.Status);
    }

    [Fact]
    public async Task History_IsScopedByOrganization()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = CreateService(clock, ScopeFactoryWith(Discovered(30)));
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();

        var startedA = await service.StartAsync(
            organizationA,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Org A Client", "references", "mixed", ["instagram"]));

        var startedB = await service.StartAsync(
            organizationB,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Org B Client", "competitors", "informative", ["youtube"]));

        await WaitForTerminalAsync(service, organizationA, startedA.Value.WorkflowId);
        await WaitForTerminalAsync(service, organizationB, startedB.Value.WorkflowId);

        var historyA = await service.ListHistoryAsync(organizationA, 20);
        var historyB = await service.ListHistoryAsync(organizationB, 20);

        Assert.True(historyA.IsSuccess);
        Assert.True(historyB.IsSuccess);
        Assert.Single(historyA.Value);
        Assert.Single(historyB.Value);
        Assert.Equal("Org A Client", historyA.Value[0].ClientName);
        Assert.Equal("Org B Client", historyB.Value[0].ClientName);
    }

    [Fact]
    public async Task Start_WithNothingDiscovered_ReturnsAnEmptyProposal()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = CreateService(clock, ScopeFactoryWith([]));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Sin referentes", "viral", "mixed", ["instagram"]));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);

        // A client with no reference accounts, or an unconfigured scraping route, must surface as an
        // honestly empty proposal — never as invented rows that look real.
        Assert.True(started.IsSuccess);
        Assert.Empty(results.Value.Items);
        Assert.Equal(0, results.Value.TotalCount);
    }

    [Fact]
    public async Task Start_ProjectsDiscoveredMetricsOntoTheProposal()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var discovered = new DiscoveredContent(
            "Un reel ganador", "resumen", "instagram", "https://ig.com/p/1", "https://ig.com/t.jpg",
            Views: 120_000, Likes: 4_000, Comments: 210, Shares: null, Saves: null,
            PublishedAtUtc: DateTimeOffset.UtcNow, SourceHandle: "@ref", SourceKey: "references");

        var service = CreateService(clock, ScopeFactoryWith([discovered]));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "viral", "mixed", ["instagram"]));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);

        var item = Assert.Single(results.Value.Items);
        Assert.Equal("Un reel ganador", item.Title);
        Assert.Equal(120_000, item.Views);
        Assert.Equal(4_000, item.Likes);
        Assert.Equal(210, item.Comments);
    }

    [Fact]
    public async Task Start_WithOwnCriterion_NoLongerReturnsSearchCriterionInvalid()
    {
        // CR-DISC-02 scenario 1: selecting "own" used to fail validation because
        // ContentWorkflowCatalog.SearchCriterionAliases had no "own" entry.
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "own", "mixed", ["instagram"]));

        Assert.True(started.IsSuccess);
        Assert.Equal("own", started.Value.SearchCriterion);
    }

    [Fact]
    public async Task Start_WithOwnCriterion_PassesTheNormalizedCriterionIntoTheDiscoveryRequest()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var spy = new SpyDiscovery(Discovered(3));
        var services = new ServiceCollection();
        services.AddScoped<IContentDiscoveryService>(_ => spy);
        services.AddScoped<IProposalGenerationStarter, StubStarter>();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var service = CreateService(clock, scopeFactory);
        var organizationId = Guid.NewGuid();

        // "propios" is the Spanish-language alias for the same canonical "own" criterion.
        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "propios", "mixed", ["instagram"]));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("own", spy.LastRequest?.SearchCriterion);
    }

    // ── candidate bank: origin and the explicit refresh ──────────────────────
    // Discovery serves from the stored bank by default. A paid run happens only when the bank is empty
    // or the operator explicitly asked for a refresh, so the flag has to survive the whole way down to
    // the discovery request and the origin has to survive the whole way back up to the operator.

    [Fact]
    public async Task Start_WithoutRefresh_AsksDiscoveryNotToRescrape()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var spy = new SpyDiscovery(Discovered(3));
        var service = CreateService(clock, ScopeFactoryWith(spy));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.False(spy.LastRequest?.ForceRefresh);
    }

    [Fact]
    public async Task Start_WithExplicitRefresh_CarriesTheOperatorsOptInIntoTheDiscoveryRequest()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var spy = new SpyDiscovery(Discovered(3));
        var service = CreateService(clock, ScopeFactoryWith(spy));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"], ForceRefresh: true));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.True(spy.LastRequest?.ForceRefresh);
    }

    [Fact]
    public async Task Status_ReportsWhereTheCandidatesCameFromAndHowOldTheStoredOnesAre()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var observedAt = new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero);
        var plan = new ContentDiscoveryPlan(Discovered(3), 0, [], [], DiscoveryOrigin.Bank, observedAt);
        var service = CreateService(clock, ScopeFactoryWith(new PlanDiscovery(plan)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("bank", terminal.ResultOrigin);
        Assert.Equal(observedAt, terminal.BankObservedAtUtc);
    }

    [Fact]
    public async Task Status_AfterAFreshRun_ReportsAFreshOrigin_WithNoStoredAge()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("fresh", terminal.ResultOrigin);
        Assert.Null(terminal.BankObservedAtUtc);
    }

    // ── start + poll execution model ─────────────────────────────────────────
    // A real scrape does not fit inside one HTTP request: production runs were aborted by the browser
    // or proxy at ~30 s ("CANCELLED BY THE CALLER after 29965 ms"), leaving the Apify run billing.
    // StartAsync therefore only opens the session; discovery runs in the background and the operator
    // polls the status endpoint.

    [Fact]
    public async Task Start_ReturnsWithoutWaitingForDiscoveryToFinish()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var gate = new TaskCompletionSource();
        var discovery = new GatedDiscovery(gate.Task, Discovered(4));
        var service = CreateService(clock, ScopeFactoryWith(discovery));
        var organizationId = Guid.NewGuid();

        // If StartAsync still awaited discovery inline, this call could never return: the stub is
        // blocked on a gate nobody has released yet.
        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        Assert.True(started.IsSuccess);
        Assert.Equal("in_progress", started.Value.Status);
        Assert.Equal(0, started.Value.TotalResults);

        var running = await service.GetStatusAsync(organizationId, started.Value.WorkflowId);
        Assert.True(running.IsSuccess);
        Assert.Equal("in_progress", running.Value.Status);
        Assert.True(running.Value.ProgressPercentage < 100);

        gate.SetResult();

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);
        Assert.Equal("completed", terminal.Status);
        Assert.Equal(4, terminal.TotalResults);
    }

    [Fact]
    public async Task BackgroundDiscovery_SurvivesCancellationOfTheRequestToken()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var gate = new TaskCompletionSource();
        var discovery = new GatedDiscovery(gate.Task, Discovered(2));
        var service = CreateService(clock, ScopeFactoryWith(discovery));
        var organizationId = Guid.NewGuid();

        using var requestCts = new CancellationTokenSource();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]),
            requestCts.Token);

        // The operator navigates away / the proxy aborts: exactly the abort that used to kill the run
        // and orphan a billing Apify actor.
        await requestCts.CancelAsync();
        gate.SetResult();

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("completed", terminal.Status);
        Assert.False(discovery.ObservedToken.IsCancellationRequested);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);
        Assert.Equal(2, results.Value.TotalCount);
    }

    [Fact]
    public async Task Status_ReachesCompletedEmpty_WhenTheChainFoundNothing()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = CreateService(clock, ScopeFactoryWith([]));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        // Empty is a SUCCESS outcome: the chain ran and genuinely found nothing.
        Assert.Equal("completed", terminal.Status);
        Assert.Equal(0, terminal.TotalResults);
        Assert.Equal(100, terminal.ProgressPercentage);
    }

    [Fact]
    public async Task Status_ReachesFailed_WhenDiscoveryThrows()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = CreateService(clock, ScopeFactoryWith(new ThrowingDiscovery()));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        // A provider failure must never read as "found nothing".
        Assert.Equal("failed", terminal.Status);
        Assert.Equal(0, terminal.TotalResults);
    }

    [Fact]
    public async Task Status_ReachesFailed_WhenEverySourceErrored()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var plan = new ContentDiscoveryPlan([], 72, ["tiktok-niche"], []);
        var service = CreateService(clock, ScopeFactoryWith(new PlanDiscovery(plan)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("failed", terminal.Status);
    }

    [Fact]
    public async Task Status_ReachesUnconfigured_WhenNoSourceHadAnEnabledActor()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var plan = new ContentDiscoveryPlan([], 72, [], ["tiktok-niche"]);
        var service = CreateService(clock, ScopeFactoryWith(new PlanDiscovery(plan)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        // "Configure this stage" is a different answer from "the provider is down" and from
        // "it ran and found nothing" (CD-13 / D12).
        Assert.Equal("unconfigured", terminal.Status);
    }

    [Fact]
    public async Task Status_StaysCompleted_WhenSomeSourcesFailedButCandidatesCameBack()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var plan = new ContentDiscoveryPlan(Discovered(3), 69, ["instagram-niche"], ["youtube-niche"]);
        var service = CreateService(clock, ScopeFactoryWith(new PlanDiscovery(plan)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        // One failing source never voids a run that produced candidates.
        Assert.Equal("completed", terminal.Status);
        Assert.Equal(3, terminal.TotalResults);
    }

    [Fact]
    public async Task Status_ForAnUnknownWorkflowId_IsACleanNotFound()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));

        // Sessions live in memory and are lost on restart; a status poll for an id this process never
        // saw must answer not-found rather than throw.
        var status = await service.GetStatusAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(status.IsFailure);
        Assert.Equal("social.workflow.not_found", status.Error.Code);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static InMemoryContentWorkflowService CreateService(
        IClock clock,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime? applicationLifetime = null) =>
        new(
            clock,
            scopeFactory,
            applicationLifetime ?? new TestApplicationLifetime(),
            NullLogger<InMemoryContentWorkflowService>.Instance);

    /// <summary>
    /// Polls the status endpoint the way the browser does, until the session leaves "in_progress".
    /// </summary>
    private static async Task<ContentWorkflowStatusResponse> WaitForTerminalAsync(
        InMemoryContentWorkflowService service,
        Guid organizationId,
        Guid workflowId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            var status = await service.GetStatusAsync(organizationId, workflowId);
            Assert.True(status.IsSuccess);

            if (status.Value.Status != "in_progress")
            {
                return status.Value;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The workflow session never reached a terminal state.");
    }

    private static IReadOnlyList<DiscoveredContent> Discovered(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new DiscoveredContent(
                $"Contenido {i}", $"resumen {i}", "instagram", $"https://ig.com/p/{i}", null,
                Views: 1_000 - i, Likes: 10, Comments: 1, Shares: null, Saves: null,
                PublishedAtUtc: DateTimeOffset.UtcNow, SourceHandle: "@ref", SourceKey: "references"))
            .ToList();

    private static IServiceScopeFactory ScopeFactoryWith(IReadOnlyList<DiscoveredContent> discovered) =>
        ScopeFactoryWith(new StubDiscovery(discovered));

    private static IServiceScopeFactory ScopeFactoryWith(IContentDiscoveryService discovery)
    {
        var services = new ServiceCollection();
        services.AddScoped<IContentDiscoveryService>(_ => discovery);
        services.AddScoped<IProposalGenerationStarter, StubStarter>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class TestApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();
    }

    private sealed class StubStarter : IProposalGenerationStarter
    {
        public Task<ProposalGenerationStarted> StartAsync(
            ProposalGenerationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProposalGenerationStarted(
                Guid.NewGuid(),
                request.Proposals.Count * request.Formats.Count));
    }

    private sealed class StubDiscovery(IReadOnlyList<DiscoveredContent> discovered) : IContentDiscoveryService
    {
        public Task<ContentDiscoveryPlan> DiscoverAsync(
            ContentDiscoveryRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ContentDiscoveryPlan(discovered, 0, [], []));
    }

    private sealed class PlanDiscovery(ContentDiscoveryPlan plan) : IContentDiscoveryService
    {
        public Task<ContentDiscoveryPlan> DiscoverAsync(
            ContentDiscoveryRequest request, CancellationToken ct = default) =>
            Task.FromResult(plan);
    }

    private sealed class ThrowingDiscovery : IContentDiscoveryService
    {
        public Task<ContentDiscoveryPlan> DiscoverAsync(
            ContentDiscoveryRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("the provider is down");
    }

    /// <summary>
    /// Stands in for a scrape that outlives the HTTP request. It records the token it was handed so a
    /// test can prove the background work is NOT wired to the aborted request token, and it waits on a
    /// gate the test releases explicitly.
    /// </summary>
    private sealed class GatedDiscovery(Task gate, IReadOnlyList<DiscoveredContent> discovered)
        : IContentDiscoveryService
    {
        public CancellationToken ObservedToken { get; private set; }

        public async Task<ContentDiscoveryPlan> DiscoverAsync(
            ContentDiscoveryRequest request, CancellationToken ct = default)
        {
            ObservedToken = ct;
            await gate;
            return new ContentDiscoveryPlan(discovered, 0, [], []);
        }
    }

    private sealed class SpyDiscovery(IReadOnlyList<DiscoveredContent> discovered) : IContentDiscoveryService
    {
        public ContentDiscoveryRequest? LastRequest { get; private set; }

        public Task<ContentDiscoveryPlan> DiscoverAsync(
            ContentDiscoveryRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new ContentDiscoveryPlan(discovered, 0, [], []));
        }
    }
}
