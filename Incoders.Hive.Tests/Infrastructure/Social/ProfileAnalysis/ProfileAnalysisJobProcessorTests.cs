using System.Collections.Concurrent;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Features.Social.ProfileAnalyzer.Analyze;
using Incoders.Hive.Infrastructure.Social.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Incoders.Hive.Tests.Infrastructure.Social.ProfileAnalysis;

/// <summary>
/// TDD unit tests for ChannelProfileAnalysisJobQueue and ProfileAnalysisBackgroundWorker.
/// Tests queue ordering, processor happy-path, and exception isolation (PA-14, PA-23).
/// </summary>
public sealed class ProfileAnalysisJobProcessorTests
{
    // ── Queue tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_Then_Dequeue_PreservesJobOrder()
    {
        var queue = new ChannelProfileAnalysisJobQueue();
        var job1 = new ProfileAnalysisJob(Guid.NewGuid(), Guid.NewGuid());
        var job2 = new ProfileAnalysisJob(Guid.NewGuid(), Guid.NewGuid());

        await queue.EnqueueAsync(job1);
        await queue.EnqueueAsync(job2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = new List<ProfileAnalysisJob>();

        await foreach (var job in queue.DequeueAllAsync(cts.Token))
        {
            received.Add(job);
            if (received.Count == 2) break;
        }

        Assert.Equal(2, received.Count);
        Assert.Equal(job1.AnalysisId, received[0].AnalysisId);
        Assert.Equal(job2.AnalysisId, received[1].AnalysisId);
    }

    // ── Processor happy-path ─────────────────────────────────────────────────

    [Fact]
    public async Task Processor_RunsOrchestratorForQueuedJob()
    {
        var analysisId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var calls = new ConcurrentBag<(Guid AnalysisId, Guid OrgId)>();
        var jobCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var analyzer = new FakeProfileAnalyzer(onRun: (aid, oid) =>
        {
            calls.Add((aid, oid));
            jobCompleted.TrySetResult(true);
        });

        var job = new ProfileAnalysisJob(analysisId, orgId);
        var queue = new ChannelProfileAnalysisJobQueue();
        await queue.EnqueueAsync(job);

        var scopeFactory = BuildScopeFactory(analyzer);
        var worker = new ProfileAnalysisBackgroundWorker(
            queue,
            scopeFactory,
            NullLogger<ProfileAnalysisBackgroundWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var _ = worker.StartAsync(cts.Token);

        // Wait deterministically for the job to complete
        await Task.WhenAny(
            jobCompleted.Task,
            Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
        cts.Cancel();

        Assert.Single(calls);
        var (callAid, callOid) = calls.Single();
        Assert.Equal(analysisId, callAid);
        Assert.Equal(orgId, callOid);
    }

    // ── Exception isolation ──────────────────────────────────────────────────

    [Fact]
    public async Task Processor_OrchestratorException_IsIsolatedAndWorkerProcessesNextJob()
    {
        var job1Id = Guid.NewGuid();
        var job2Id = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        // ConcurrentBag is thread-safe — both jobs may run in parallel with MaxConcurrency=3
        var calls = new ConcurrentBag<Guid>();

        // TCS lets us deterministically wait for job2 to complete rather than relying on Task.Delay
        var job2Completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var analyzer = new FakeProfileAnalyzer(onRun: (aid, _) =>
        {
            calls.Add(aid);
            if (aid == job1Id)
                throw new InvalidOperationException("Simulated crash on job1");
            if (aid == job2Id)
                job2Completed.TrySetResult(true);
        });

        var queue = new ChannelProfileAnalysisJobQueue();
        await queue.EnqueueAsync(new ProfileAnalysisJob(job1Id, orgId));
        await queue.EnqueueAsync(new ProfileAnalysisJob(job2Id, orgId));

        var scopeFactory = BuildScopeFactory(analyzer);
        var worker = new ProfileAnalysisBackgroundWorker(
            queue,
            scopeFactory,
            NullLogger<ProfileAnalysisBackgroundWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var _ = worker.StartAsync(cts.Token);

        // Wait deterministically for job2 to complete (or timeout after 5s)
        var completedInTime = await Task.WhenAny(
            job2Completed.Task,
            Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));

        cts.Cancel();

        // Both jobs must have been attempted — worker survived job1's crash
        Assert.Contains(job1Id, calls);
        Assert.Contains(job2Id, calls);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IServiceScopeFactory BuildScopeFactory(IProfileAnalyzer analyzer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(analyzer);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeProfileAnalyzer : IProfileAnalyzer
    {
        private readonly Action<Guid, Guid> _onRun;

        public FakeProfileAnalyzer(Action<Guid, Guid> onRun) => _onRun = onRun;

        public Task RunQueuedAsync(
            Guid analysisId,
            Guid organizationId,
            CancellationToken cancellationToken = default)
        {
            _onRun(analysisId, organizationId);
            return Task.CompletedTask;
        }
    }
}
