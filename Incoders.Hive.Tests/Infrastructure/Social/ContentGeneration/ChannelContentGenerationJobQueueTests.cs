using Incoders.Hive.Infrastructure.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// TDD tests for <see cref="ChannelContentGenerationJobQueue"/>.
/// RED: references type that doesn't exist yet.
/// </summary>
public sealed class ChannelContentGenerationJobQueueTests
{
    // ─── Enqueue then dequeue returns same id ────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_ThenDequeue_ReturnsSameJobId()
    {
        var queue = new ChannelContentGenerationJobQueue();
        var jobId = Guid.NewGuid();

        await queue.EnqueueAsync(jobId, CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        Guid dequeued = Guid.Empty;
        await foreach (var id in queue.DequeueAllAsync(cts.Token))
        {
            dequeued = id;
            break; // take first item
        }

        Assert.Equal(jobId, dequeued);
    }

    // ─── Multiple enqueues dequeue in FIFO order ─────────────────────────────

    [Fact]
    public async Task EnqueueMultiple_DequeueReturnsFifo()
    {
        var queue = new ChannelContentGenerationJobQueue();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await queue.EnqueueAsync(id1, CancellationToken.None);
        await queue.EnqueueAsync(id2, CancellationToken.None);

        var dequeued = new List<Guid>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await foreach (var id in queue.DequeueAllAsync(cts.Token))
        {
            dequeued.Add(id);
            if (dequeued.Count == 2) break;
        }

        Assert.Equal(2, dequeued.Count);
        Assert.Equal(id1, dequeued[0]);
        Assert.Equal(id2, dequeued[1]);
    }

    // ─── Cancellation token is honored ───────────────────────────────────────

    [Fact]
    public async Task DequeueAllAsync_CancellationBeforeEnqueue_StopsEnumeration()
    {
        var queue = new ChannelContentGenerationJobQueue();
        using var cts = new CancellationTokenSource();

        var dequeued = new List<Guid>();
        var dequeueTask = Task.Run(async () =>
        {
            await foreach (var id in queue.DequeueAllAsync(cts.Token))
            {
                dequeued.Add(id);
            }
        });

        // Cancel before anything is enqueued
        cts.Cancel();

        // Should complete without hanging
        await Task.WhenAny(dequeueTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.True(dequeueTask.IsCompleted || dequeueTask.IsCanceled || dequeueTask.IsFaulted);
        Assert.Empty(dequeued);
    }

    // ─── Concurrent producers and consumers ──────────────────────────────────

    [Fact]
    public async Task ConcurrentProducersConsumers_AllItemsDequeued()
    {
        var queue = new ChannelContentGenerationJobQueue(capacity: 50);
        var producedIds = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        var consumedIds = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        const int itemCount = 20;

        // Produce 20 items concurrently
        var produceTask = Parallel.ForEachAsync(
            Enumerable.Range(0, itemCount),
            async (_, ct) =>
            {
                var id = Guid.NewGuid();
                producedIds.Add(id);
                await queue.EnqueueAsync(id, ct);
            });

        await produceTask;

        // Consume all items
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var id in queue.DequeueAllAsync(cts.Token))
        {
            consumedIds.Add(id);
            if (consumedIds.Count == itemCount) break;
        }

        Assert.Equal(itemCount, consumedIds.Count);
        // Every produced id was consumed
        foreach (var id in producedIds)
        {
            Assert.Contains(id, consumedIds);
        }
    }
}
