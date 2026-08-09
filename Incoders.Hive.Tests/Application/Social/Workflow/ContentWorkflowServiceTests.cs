using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.Workflow;
using Incoders.Hive.Infrastructure.Social.Workflow;
using Incoders.Hive.Tests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Incoders.Hive.Tests.Application.Social.Workflow;

public sealed class ContentWorkflowServiceTests
{
    [Fact]
    public async Task Start_ThenGetResults_ReturnsPaginatedEnvelope()
    {
        var clock = new TestClock(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
        var service = new InMemoryContentWorkflowService(clock, ScopeFactoryWith(Discovered(30)));
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
        var service = new InMemoryContentWorkflowService(clock, ScopeFactoryWith(Discovered(30)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(
                Guid.NewGuid(),
                "Acme Social",
                "niche",
                "educational",
                ["tiktok"]));

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
        var service = new InMemoryContentWorkflowService(clock, ScopeFactoryWith(Discovered(30)));
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();

        await service.StartAsync(
            organizationA,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Org A Client", "references", "mixed", ["instagram"]));

        await service.StartAsync(
            organizationB,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Org B Client", "competitors", "informative", ["youtube"]));

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
        var service = new InMemoryContentWorkflowService(clock, ScopeFactoryWith([]));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Sin referentes", "viral", "mixed", ["instagram"]));

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
            Views: 120_000, Likes: 4_000, Comments: 210, SourceHandle: "@ref");

        var service = new InMemoryContentWorkflowService(clock, ScopeFactoryWith([discovered]));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "viral", "mixed", ["instagram"]));

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);

        var item = Assert.Single(results.Value.Items);
        Assert.Equal("Un reel ganador", item.Title);
        Assert.Equal(120_000, item.Views);
        Assert.Equal(4_000, item.Likes);
        Assert.Equal(210, item.Comments);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<DiscoveredContent> Discovered(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new DiscoveredContent(
                $"Contenido {i}", $"resumen {i}", "instagram", $"https://ig.com/p/{i}", null,
                Views: 1_000 - i, Likes: 10, Comments: 1, SourceHandle: "@ref"))
            .ToList();

    private static IServiceScopeFactory ScopeFactoryWith(IReadOnlyList<DiscoveredContent> discovered)
    {
        var services = new ServiceCollection();
        services.AddScoped<IContentDiscoveryService>(_ => new StubDiscovery(discovered));
        services.AddScoped<IProposalGenerationStarter, StubStarter>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
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
        public Task<IReadOnlyList<DiscoveredContent>> DiscoverAsync(
            ContentDiscoveryRequest request, CancellationToken ct = default) =>
            Task.FromResult(discovered);
    }
}
