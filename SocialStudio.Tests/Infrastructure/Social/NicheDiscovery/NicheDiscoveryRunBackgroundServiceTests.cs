using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Domain.Social.NicheDiscovery;
using SocialStudio.Infrastructure.Social.NicheDiscovery;
using Microsoft.Extensions.DependencyInjection;

namespace SocialStudio.Tests.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// WU-11a: <see cref="NicheDiscoveryRunBackgroundService"/> dequeues and invokes
/// <see cref="INicheDiscoveryRunProcessor"/> in its own DI scope per job (mirrors
/// <c>ProfileAnalysisBackgroundWorker</c>'s per-job scope discipline, obs #518).
/// </summary>
public sealed class NicheDiscoveryRunBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_DequeuesAndInvokesTheProcessor_WithTheSameOrganizationAndRunId()
    {
        var orgId = Guid.NewGuid();
        var runId = DiscoveryRunId.New();
        var queue = new NicheDiscoveryRunQueue();
        await queue.EnqueueAsync(orgId, runId, CancellationToken.None);

        var processor = new RecordingProcessor();
        var services = new ServiceCollection();
        services.AddSingleton<INicheDiscoveryRunProcessor>(processor);
        await using var provider = services.BuildServiceProvider();

        var service = new NicheDiscoveryRunBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>());

        using var cts = new CancellationTokenSource();
        var runTask = service.StartAsync(cts.Token);

        await processor.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(orgId, processor.LastOrganizationId);
        Assert.Equal(runId, processor.LastRunId);
    }

    private sealed class RecordingProcessor : INicheDiscoveryRunProcessor
    {
        public TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid LastOrganizationId { get; private set; }

        public DiscoveryRunId LastRunId { get; private set; }

        public Task RunAsync(Guid organizationId, DiscoveryRunId runId, CancellationToken ct = default)
        {
            LastOrganizationId = organizationId;
            LastRunId = runId;
            Invoked.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
