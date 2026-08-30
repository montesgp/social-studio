using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Social.ContentDiscovery;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;
using Incoders.Hive.Application.Features.Social.ContentGeneration.Ideas;
using Incoders.Hive.Application.Features.Social.Workflow;
using Incoders.Hive.Domain.Social.Workflow;
using Incoders.Hive.Infrastructure.Identity;
using Incoders.Hive.Infrastructure.Persistence.Postgres.Repositories;
using Incoders.Hive.Infrastructure.Social.Workflow;
using Incoders.Hive.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Application.Social.Workflow;

/// <summary>
/// Approval/behavior tests for <see cref="EfContentWorkflowService"/> (Phase 5.9 facade rewrite): same
/// discovery orchestration and progress-ramp behavior as the deleted <c>InMemoryContentWorkflowService</c>,
/// now persisting through <see cref="EfContentWorkflowSessionRepository"/> (EF InMemory provider, same
/// harness convention as this repo's other repository tests) instead of a
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>. The mock generation
/// paths the old service exposed (preview / actions / package) are gone from the interface — spec removes
/// them, not ported here.
/// </summary>
public sealed class ContentWorkflowServiceTests
{
    [Fact]
    public async Task Start_ThenGetResults_ReturnsPaginatedEnvelope()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(30)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "CM Team", "viral", "mixed", ["tiktok", "instagram"]));

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
    public async Task SetSelection_PersistsSelectedResultIds()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(5)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme Social", "niche", "educational", ["tiktok"]));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 5);
        var selectedIds = results.Value.Items.Take(2).Select(x => x.Id).ToList();

        var selection = await service.SetSelectionAsync(organizationId, started.Value.WorkflowId, selectedIds);
        var refreshed = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 5);

        Assert.True(selection.IsSuccess);
        Assert.Equal(2, selection.Value.SelectedCount);
        Assert.Equal(2, refreshed.Value.Items.Count(x => x.IsSelected));
    }

    [Fact]
    public async Task SetSelection_WithUnknownResultId_IsRejected()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var selection = await service.SetSelectionAsync(organizationId, started.Value.WorkflowId, [Guid.NewGuid()]);

        Assert.True(selection.IsFailure);
        Assert.Equal(ContentWorkflowErrors.ResultNotFound, selection.Error);
    }

    [Fact]
    public async Task History_IsScopedByOrganization()
    {
        var clock = new TestClock(DateTime.UtcNow);
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
    public async Task ListHistory_WithClientId_ReturnsOnlyThatClientsEntries()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));
        var organizationId = Guid.NewGuid();
        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();

        var startedA = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(clientA, "Client A", "references", "mixed", ["instagram"]));
        var startedB = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(clientB, "Client B", "competitors", "informative", ["youtube"]));

        await WaitForTerminalAsync(service, organizationId, startedA.Value.WorkflowId);
        await WaitForTerminalAsync(service, organizationId, startedB.Value.WorkflowId);

        var filtered = await service.ListHistoryAsync(organizationId, 20, clientA);
        var unfiltered = await service.ListHistoryAsync(organizationId, 20);

        Assert.True(filtered.IsSuccess);
        Assert.Single(filtered.Value);
        Assert.Equal("Client A", filtered.Value[0].ClientName);
        Assert.True(unfiltered.IsSuccess);
        Assert.Equal(2, unfiltered.Value.Count);
    }

    [Fact]
    public async Task GetHistory_ExposesPerCandidateEnqueueStatus()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var (service, repository) = CreateServiceWithRepository(clock, ScopeFactoryWith(Discovered(2)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));
        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);
        var candidateId = results.Value.Items[0].Id;

        await repository.UpdateCandidateStatusAsync(
            candidateId, WorkflowCandidateStatus.Analyzing, analysisId: Guid.NewGuid(), batchId: null, failureCode: null, CancellationToken.None);

        var history = await service.GetHistoryAsync(organizationId, started.Value.WorkflowId);

        Assert.True(history.IsSuccess);
        var candidateStatus = Assert.Single(history.Value.Candidates, c => c.CandidateId == candidateId);
        Assert.Equal("Analyzing", candidateStatus.Status);
        Assert.Null(candidateStatus.FailureCode);
    }

    [Fact]
    public async Task GetHistory_SurfacesFailureCodeWhenACandidatesAnalysisFails()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var (service, repository) = CreateServiceWithRepository(clock, ScopeFactoryWith(Discovered(1)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));
        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);
        var candidateId = results.Value.Items[0].Id;

        await repository.UpdateCandidateStatusAsync(
            candidateId, WorkflowCandidateStatus.AnalysisFailed, analysisId: null, batchId: null,
            failureCode: "analysis_timeout", CancellationToken.None);

        var history = await service.GetHistoryAsync(organizationId, started.Value.WorkflowId);

        var candidateStatus = Assert.Single(history.Value.Candidates, c => c.CandidateId == candidateId);
        Assert.Equal("AnalysisFailed", candidateStatus.Status);
        Assert.Equal("analysis_timeout", candidateStatus.FailureCode);
    }

    [Fact]
    public async Task GetHistory_BeforeAnyEnqueue_ExcludesUnsubmittedCandidates()
    {
        // Corrects the prior bug this test used to encode: discovery persists candidate rows so the
        // operator can select/compose them, but a candidate that was only discovered — never composed
        // and enqueued — must NOT appear as "Queued" on the history read surface (that falsely implies
        // it was submitted to the real analysis-then-batch pipeline). See
        // ContentWorkflowContracts.ContentWorkflowHistoryResponse.Candidates docstring: "Empty until the
        // operator composes and enqueues at least one candidate for this workflow."
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));
        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);
        var history = await service.GetHistoryAsync(organizationId, started.Value.WorkflowId);

        Assert.True(history.IsSuccess);
        // Discovery still persisted all 3 candidates — selection/composition still has something to work
        // with — but none of them were ever composed and enqueued, so none belong on the history surface.
        Assert.Equal(3, results.Value.TotalCount);
        Assert.Empty(history.Value.Candidates);
    }

    [Fact]
    public async Task Start_WithNothingDiscovered_ReturnsAnEmptyProposal()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith([]));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Sin referentes", "viral", "mixed", ["instagram"]));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);

        Assert.True(started.IsSuccess);
        Assert.Empty(results.Value.Items);
        Assert.Equal(0, results.Value.TotalCount);
    }

    [Fact]
    public async Task Start_ProjectsDiscoveredMetricsOntoTheProposal()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var discovered = new DiscoveredContent(
            "Un reel ganador", "resumen", "instagram", "https://ig.com/p/1", "https://ig.com/t.jpg",
            Views: 120_000, Likes: 4_000, Comments: 210, Shares: null, Saves: null,
            PublishedAtUtc: DateTimeOffset.UtcNow, SourceHandle: "@ref", SourceKey: "references");

        var service = CreateService(clock, ScopeFactoryWith([discovered]));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "viral", "mixed", ["instagram"]));

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
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "own", "mixed", ["instagram"]));

        Assert.True(started.IsSuccess);
        Assert.Equal("own", started.Value.SearchCriterion);
    }

    [Fact]
    public async Task Start_WithOwnCriterion_PassesTheNormalizedCriterionIntoTheDiscoveryRequest()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var spy = new SpyDiscovery(Discovered(3));
        var service = CreateService(clock, ScopeFactoryWith(spy));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "propios", "mixed", ["instagram"]));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("own", spy.LastRequest?.SearchCriterion);
    }

    [Fact]
    public async Task Start_WithoutRefresh_AsksDiscoveryNotToRescrape()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var spy = new SpyDiscovery(Discovered(3));
        var service = CreateService(clock, ScopeFactoryWith(spy));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.False(spy.LastRequest?.ForceRefresh);
    }

    [Fact]
    public async Task Start_WithExplicitRefresh_CarriesTheOperatorsOptInIntoTheDiscoveryRequest()
    {
        var clock = new TestClock(DateTime.UtcNow);
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
    public async Task Start_WithExplicitMaxResults_CapsTheDiscoveryRequest()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var spy = new SpyDiscovery(Discovered(3));
        var service = CreateService(clock, ScopeFactoryWith(spy));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"], MaxResults: 25));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal(25, spy.LastRequest?.MaxResults);
    }

    [Fact]
    public async Task Start_WithoutMaxResults_FallsBackToTheDefaultResultCount()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var spy = new SpyDiscovery(Discovered(3));
        var service = CreateService(clock, ScopeFactoryWith(spy));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal(72, spy.LastRequest?.MaxResults);
    }

    [Fact]
    public async Task Status_ReportsWhereTheCandidatesCameFromAndHowOldTheStoredOnesAre()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var observedAt = new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero);
        var plan = new ContentDiscoveryPlan(Discovered(3), 0, [], [], DiscoveryOrigin.Bank, observedAt);
        var service = CreateService(clock, ScopeFactoryWith(new PlanDiscovery(plan)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("bank", terminal.ResultOrigin);
        Assert.Equal(observedAt, terminal.BankObservedAtUtc);
    }

    [Fact]
    public async Task Status_AfterAFreshRun_ReportsAFreshOrigin_WithNoStoredAge()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("fresh", terminal.ResultOrigin);
        Assert.Null(terminal.BankObservedAtUtc);
    }

    [Fact]
    public async Task Start_ReturnsWithoutWaitingForDiscoveryToFinish()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var gate = new TaskCompletionSource();
        var discovery = new GatedDiscovery(gate.Task, Discovered(4));
        var service = CreateService(clock, ScopeFactoryWith(discovery));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

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
        var clock = new TestClock(DateTime.UtcNow);
        var gate = new TaskCompletionSource();
        var discovery = new GatedDiscovery(gate.Task, Discovered(2));
        var service = CreateService(clock, ScopeFactoryWith(discovery));
        var organizationId = Guid.NewGuid();

        using var requestCts = new CancellationTokenSource();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]),
            requestCts.Token);

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
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith([]));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("completed", terminal.Status);
        Assert.Equal(0, terminal.TotalResults);
        Assert.Equal(100, terminal.ProgressPercentage);
    }

    [Fact]
    public async Task Status_ReachesFailed_WhenDiscoveryThrows()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(new ThrowingDiscovery()));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("failed", terminal.Status);
        Assert.Equal(0, terminal.TotalResults);
    }

    [Fact]
    public async Task Status_ReachesFailed_WhenEverySourceErrored()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var plan = new ContentDiscoveryPlan([], 72, ["tiktok-niche"], []);
        var service = CreateService(clock, ScopeFactoryWith(new PlanDiscovery(plan)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("failed", terminal.Status);
    }

    [Fact]
    public async Task Status_ReachesUnconfigured_WhenNoSourceHadAnEnabledActor()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var plan = new ContentDiscoveryPlan([], 72, [], ["tiktok-niche"]);
        var service = CreateService(clock, ScopeFactoryWith(new PlanDiscovery(plan)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("unconfigured", terminal.Status);
    }

    [Fact]
    public async Task Status_StaysCompleted_WhenSomeSourcesFailedButCandidatesCameBack()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var plan = new ContentDiscoveryPlan(Discovered(3), 69, ["instagram-niche"], ["youtube-niche"]);
        var service = CreateService(clock, ScopeFactoryWith(new PlanDiscovery(plan)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal("completed", terminal.Status);
        Assert.Equal(3, terminal.TotalResults);
    }

    [Fact]
    public async Task Status_ForAnUnknownWorkflowId_IsACleanNotFound()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));

        var status = await service.GetStatusAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(status.IsFailure);
        Assert.Equal("social.workflow.not_found", status.Error.Code);
    }

    [Fact]
    public async Task Start_WithAiIdeasOrigin_NeverCallsDiscovery()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var spy = new SpyDiscovery(Discovered(3));
        var service = CreateService(clock, IdeaScopeFactory(spy, new SpyIdeaSource(3)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"], OriginMode: "ai-ideas"));

        Assert.True(started.IsSuccess);
        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Null(spy.LastRequest);
    }

    [Fact]
    public async Task Start_WithAiIdeasOrigin_FillsTheSessionFromTheIdeaChain()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var source = new SpyIdeaSource(4);
        var service = CreateService(clock, IdeaScopeFactory(new SpyDiscovery([]), source));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"], OriginMode: "ai-ideas"));

        var terminal = await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);
        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);

        Assert.Equal("completed", terminal.Status);
        Assert.Equal(4, results.Value.TotalCount);
        Assert.Equal("idea-1", results.Value.Items[0].Title);
        Assert.All(results.Value.Items, item => Assert.Equal(0, item.Views));
        Assert.All(results.Value.Items, item => Assert.Equal(string.Empty, item.VideoUrl));
    }

    [Fact]
    public async Task Start_WithAiIdeasOrigin_AsksOnlyForInventedIdeas()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var source = new SpyIdeaSource(2);
        var service = CreateService(clock, IdeaScopeFactory(new SpyDiscovery([]), source));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"], OriginMode: "ai-ideas"));

        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        Assert.Equal(IdeaOrigin.Invented, source.LastRequest?.OriginFilter);
    }

    [Fact]
    public async Task Start_WithAiIdeasOrigin_GivesEveryRunItsOwnVariationSeed()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var source = new SpyIdeaSource(2);
        var service = CreateService(clock, IdeaScopeFactory(new SpyDiscovery([]), source));
        var organizationId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        var first = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(clientId, "Acme", "niche", "mixed", ["tiktok"], OriginMode: "ai-ideas"));
        await WaitForTerminalAsync(service, organizationId, first.Value.WorkflowId);
        var firstSeed = source.LastRequest?.VariationSeed;

        var second = await service.StartAsync(
            organizationId, new ContentWorkflowStartInput(clientId, "Acme", "niche", "mixed", ["tiktok"], OriginMode: "ai-ideas"));
        await WaitForTerminalAsync(service, organizationId, second.Value.WorkflowId);

        Assert.False(string.IsNullOrWhiteSpace(firstSeed));
        Assert.NotEqual(firstSeed, source.LastRequest?.VariationSeed);
    }

    [Fact]
    public async Task Start_WithAiIdeasOrigin_DoesNotRequireANetwork()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, IdeaScopeFactory(new SpyDiscovery([]), new SpyIdeaSource(2)));

        var started = await service.StartAsync(
            Guid.NewGuid(),
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", [], OriginMode: "ai-ideas"));

        Assert.True(started.IsSuccess);
    }

    [Fact]
    public async Task Start_WithDiscoveryOrigin_StillRequiresANetwork()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));

        var started = await service.StartAsync(
            Guid.NewGuid(), new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", []));

        Assert.True(started.IsFailure);
        Assert.Equal(ContentWorkflowErrors.NetworkSourcesRequired, started.Error);
    }

    [Fact]
    public async Task Start_WithAnUnknownOrigin_IsRejected()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));

        var started = await service.StartAsync(
            Guid.NewGuid(),
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"], OriginMode: "telepatia"));

        Assert.True(started.IsFailure);
        Assert.Equal(ContentWorkflowErrors.OriginModeInvalid, started.Error);
    }

    [Fact]
    public async Task GetResults_ExposesDiscoveredStatus_UntilComposedAndEnqueued()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(3)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));
        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);

        Assert.True(results.IsSuccess);
        Assert.All(results.Value.Items, item => Assert.Equal(WorkflowCandidateStatus.Discovered.ToString(), item.Status));
    }

    [Fact]
    public async Task RecoverCandidate_FromBatchStarted_MovesToRecovered_AndIsReflectedInResults()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var (service, repository) = CreateServiceWithRepository(clock, ScopeFactoryWith(Discovered(1)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));
        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);
        var candidateId = results.Value.Items[0].Id;

        // Simulate the sequencer having already driven this candidate to the "processed" terminal state.
        await repository.UpdateCandidateStatusAsync(candidateId, WorkflowCandidateStatus.BatchStarted, null, null, null);

        var recovered = await service.RecoverCandidateAsync(organizationId, started.Value.WorkflowId, candidateId);
        var refreshed = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);

        Assert.True(recovered.IsSuccess);
        Assert.Equal(WorkflowCandidateStatus.Recovered.ToString(), recovered.Value.Status);
        Assert.Equal(WorkflowCandidateStatus.Recovered.ToString(), refreshed.Value.Items[0].Status);
    }

    [Fact]
    public async Task RecoverCandidate_WhenNotBatchStarted_IsRejected()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var service = CreateService(clock, ScopeFactoryWith(Discovered(1)));
        var organizationId = Guid.NewGuid();

        var started = await service.StartAsync(
            organizationId,
            new ContentWorkflowStartInput(Guid.NewGuid(), "Acme", "niche", "mixed", ["tiktok"]));
        await WaitForTerminalAsync(service, organizationId, started.Value.WorkflowId);

        var results = await service.GetResultsAsync(organizationId, started.Value.WorkflowId, 1, 10);
        var candidateId = results.Value.Items[0].Id;

        var recovered = await service.RecoverCandidateAsync(organizationId, started.Value.WorkflowId, candidateId);

        Assert.True(recovered.IsFailure);
        Assert.Equal(WorkflowCandidateErrors.InvalidTransition, recovered.Error);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    private static EfContentWorkflowService CreateService(
        IClock clock,
        Action<IServiceCollection> configure,
        IHostApplicationLifetime? applicationLifetime = null) =>
        CreateServiceWithRepository(clock, configure, applicationLifetime).Service;

    private static (EfContentWorkflowService Service, IContentWorkflowSessionRepository Repository) CreateServiceWithRepository(
        IClock clock,
        Action<IServiceCollection> configure,
        IHostApplicationLifetime? applicationLifetime = null)
    {
        var dbName = $"content-workflow-service-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<AppIdentityDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IContentWorkflowSessionRepository, EfContentWorkflowSessionRepository>();
        configure(services);

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var rootScope = provider.CreateScope();
        var repository = rootScope.ServiceProvider.GetRequiredService<IContentWorkflowSessionRepository>();

        var service = new EfContentWorkflowService(
            repository,
            clock,
            scopeFactory,
            applicationLifetime ?? new TestApplicationLifetime(),
            NullLogger<EfContentWorkflowService>.Instance);

        return (service, repository);
    }

    private static async Task<ContentWorkflowStatusResponse> WaitForTerminalAsync(
        EfContentWorkflowService service, Guid organizationId, Guid workflowId)
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

    private sealed class SpyIdeaSource(int count) : IContentIdeaSource
    {
        public string SourceKey => "ai-agent";
        public int Order => 4;
        public IdeaOrigin Origin => IdeaOrigin.Invented;
        public ContentIdeaRequest? LastRequest { get; private set; }

        public Task<IReadOnlyList<ContentIdea>> DrawAsync(ContentIdeaRequest request, CancellationToken ct)
        {
            LastRequest = request;

            IReadOnlyList<ContentIdea> ideas = Enumerable.Range(1, count)
                .Select(i => new ContentIdea($"idea-{i}", $"resumen {i}", "ai-agent"))
                .ToList();

            return Task.FromResult(ideas);
        }
    }

    private static IReadOnlyList<DiscoveredContent> Discovered(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new DiscoveredContent(
                $"Contenido {i}", $"resumen {i}", "instagram", $"https://ig.com/p/{i}", null,
                Views: 1_000 - i, Likes: 10, Comments: 1, Shares: null, Saves: null,
                PublishedAtUtc: DateTimeOffset.UtcNow, SourceHandle: "@ref", SourceKey: "references"))
            .ToList();

    private static Action<IServiceCollection> ScopeFactoryWith(IReadOnlyList<DiscoveredContent> discovered) =>
        ScopeFactoryWith(new StubDiscovery(discovered));

    private static Action<IServiceCollection> ScopeFactoryWith(IContentDiscoveryService discovery) =>
        services => services.AddScoped(_ => discovery);

    private static Action<IServiceCollection> IdeaScopeFactory(IContentDiscoveryService discovery, IContentIdeaSource ideaSource) =>
        services =>
        {
            services.AddScoped(_ => discovery);
            services.AddScoped(_ => ideaSource);
            services.AddScoped<ContentIdeaChain>();
        };

    private sealed class TestApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();
    }

    private sealed class StubDiscovery(IReadOnlyList<DiscoveredContent> discovered) : IContentDiscoveryService
    {
        public Task<ContentDiscoveryPlan> DiscoverAsync(ContentDiscoveryRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ContentDiscoveryPlan(discovered, 0, [], []));
    }

    private sealed class PlanDiscovery(ContentDiscoveryPlan plan) : IContentDiscoveryService
    {
        public Task<ContentDiscoveryPlan> DiscoverAsync(ContentDiscoveryRequest request, CancellationToken ct = default) =>
            Task.FromResult(plan);
    }

    private sealed class ThrowingDiscovery : IContentDiscoveryService
    {
        public Task<ContentDiscoveryPlan> DiscoverAsync(ContentDiscoveryRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("the provider is down");
    }

    private sealed class GatedDiscovery(Task gate, IReadOnlyList<DiscoveredContent> discovered) : IContentDiscoveryService
    {
        public CancellationToken ObservedToken { get; private set; }

        public async Task<ContentDiscoveryPlan> DiscoverAsync(ContentDiscoveryRequest request, CancellationToken ct = default)
        {
            ObservedToken = ct;
            await gate;
            return new ContentDiscoveryPlan(discovered, 0, [], []);
        }
    }

    private sealed class SpyDiscovery(IReadOnlyList<DiscoveredContent> discovered) : IContentDiscoveryService
    {
        public ContentDiscoveryRequest? LastRequest { get; private set; }

        public Task<ContentDiscoveryPlan> DiscoverAsync(ContentDiscoveryRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new ContentDiscoveryPlan(discovered, 0, [], []));
        }
    }
}
