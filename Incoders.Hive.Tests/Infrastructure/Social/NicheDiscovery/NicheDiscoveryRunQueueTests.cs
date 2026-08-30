using Incoders.Hive.Domain.Social.NicheDiscovery;
using Incoders.Hive.Infrastructure.Social.NicheDiscovery;

namespace Incoders.Hive.Tests.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// WU-11a: <see cref="NicheDiscoveryRunQueue"/> — in-memory, Channel-backed, start-and-poll (design
/// §10). Mirrors <c>ChannelProfileAnalysisJobQueue</c>.
/// </summary>
public sealed class NicheDiscoveryRunQueueTests
{
    [Fact]
    public async Task EnqueueThenDequeue_RoundTripsTheOrganizationAndRunId()
    {
        var queue = new NicheDiscoveryRunQueue();
        var orgId = Guid.NewGuid();
        var runId = DiscoveryRunId.New();

        await queue.EnqueueAsync(orgId, runId, CancellationToken.None);
        var dequeued = await queue.DequeueAsync(CancellationToken.None);

        Assert.NotNull(dequeued);
        Assert.Equal(orgId, dequeued!.Value.OrganizationId);
        Assert.Equal(runId, dequeued.Value.RunId);
    }

    [Fact]
    public async Task DequeueAsync_PreservesFifoOrder()
    {
        var queue = new NicheDiscoveryRunQueue();
        var orgId = Guid.NewGuid();
        var first = DiscoveryRunId.New();
        var second = DiscoveryRunId.New();

        await queue.EnqueueAsync(orgId, first, CancellationToken.None);
        await queue.EnqueueAsync(orgId, second, CancellationToken.None);

        var dequeuedFirst = await queue.DequeueAsync(CancellationToken.None);
        var dequeuedSecond = await queue.DequeueAsync(CancellationToken.None);

        Assert.Equal(first, dequeuedFirst!.Value.RunId);
        Assert.Equal(second, dequeuedSecond!.Value.RunId);
    }

    [Fact]
    public async Task DequeueAsync_ReturnsNull_WhenCancelledBeforeAnItemArrives()
    {
        var queue = new NicheDiscoveryRunQueue();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var dequeued = await queue.DequeueAsync(cts.Token);

        Assert.Null(dequeued);
    }
}
