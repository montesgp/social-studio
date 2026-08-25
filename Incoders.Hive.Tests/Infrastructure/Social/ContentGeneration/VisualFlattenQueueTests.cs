using Incoders.Hive.Application.Abstractions.Social.ContentStudio;
using Incoders.Hive.Infrastructure.Social.ContentGeneration;

namespace Incoders.Hive.Tests.Infrastructure.Social.ContentGeneration;

/// <summary>
/// The queue that carries a saved studio item to the flatten worker.
///
/// <para>
/// Its contract is shaped by one rule: a save must never fail, slow down, or block because its
/// derived preview could not be scheduled. So enqueueing is non-blocking, duplicate work is
/// collapsed, and an overflowing queue drops rather than waits.
/// </para>
/// </summary>
public sealed class VisualFlattenQueueTests
{
    private static readonly Guid Org = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void AnItemQueuedTwiceIsOnlyRenderedOnce()
    {
        // Autosave fires a save per edit tick; flattening each one would pin the render semaphore.
        var queue = new VisualFlattenQueue();
        var item = Guid.NewGuid();

        Assert.True(queue.Enqueue(new VisualFlattenRequest(Org, item)));
        Assert.False(queue.Enqueue(new VisualFlattenRequest(Org, item)));
        Assert.False(queue.Enqueue(new VisualFlattenRequest(Org, item)));
    }

    [Fact]
    public void DifferentItemsAreQueuedIndependently()
    {
        var queue = new VisualFlattenQueue();

        Assert.True(queue.Enqueue(new VisualFlattenRequest(Org, Guid.NewGuid())));
        Assert.True(queue.Enqueue(new VisualFlattenRequest(Org, Guid.NewGuid())));
    }

    [Fact]
    public async Task AnItemBecomesQueueableAgainOnceTheWorkerPicksItUp()
    {
        // The de-dup marker clears BEFORE the render, so an edit landing mid-render still schedules
        // a fresh pass instead of being silently skipped.
        var queue = new VisualFlattenQueue();
        var item = Guid.NewGuid();
        queue.Enqueue(new VisualFlattenRequest(Org, item));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = queue.ReadAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(item, reader.Current.ItemId);

        Assert.True(queue.Enqueue(new VisualFlattenRequest(Org, item)));
    }

    [Fact]
    public async Task TheWorkerReceivesTheOrganizationSoItCanRescope()
    {
        var queue = new VisualFlattenQueue();
        var item = Guid.NewGuid();
        queue.Enqueue(new VisualFlattenRequest(Org, item));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = queue.ReadAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await reader.MoveNextAsync());

        Assert.Equal(Org, reader.Current.OrganizationId);
    }

    [Fact]
    public void OverflowDropsInsteadOfBlocking()
    {
        // 256 is the cap. Enqueue well past it on the calling thread: if the channel ever blocked
        // instead of dropping, this test would hang rather than fail — which is exactly the
        // behaviour a save must never inherit.
        var queue = new VisualFlattenQueue();

        var accepted = 0;
        for (var i = 0; i < 600; i++)
        {
            if (queue.Enqueue(new VisualFlattenRequest(Org, Guid.NewGuid())))
            {
                accepted++;
            }
        }

        Assert.Equal(256, accepted);
    }
}
