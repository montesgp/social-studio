using System.Collections.Concurrent;
using System.Threading.Channels;
using SocialStudio.Application.Abstractions.Social.ContentStudio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SocialStudio.Infrastructure.Social.ContentGeneration;

/// <summary>
/// In-process, bounded, de-duplicating queue of pending flatten passes.
///
/// <para>
/// Deliberately in-process and not a durable job table. The queued work rebuilds a DERIVED preview
/// whose source (the markup) is already committed, so losing a queued entry on restart costs a
/// stale preview that the next save regenerates — not authored work. A durable table would add a
/// migration and a second job lifecycle for something that is safe to drop.
/// </para>
///
/// <para>
/// De-duplication matters more than ordering here: an operator dragging a text box fires a save per
/// autosave tick, and flattening every one of those would keep the render semaphore permanently
/// busy. Only one pass per item is ever in flight or pending; a save that arrives while its item is
/// already queued is a no-op, and the worker clears the marker before it renders so a save landing
/// mid-render still queues a fresh pass.
/// </para>
/// </summary>
public sealed class VisualFlattenQueue : IVisualFlattenQueue
{
    /// <summary>
    /// Cap on pending items. Generous next to the number of items one operator can edit, but finite
    /// so a runaway producer cannot grow the queue without bound.
    /// </summary>
    private const int Capacity = 256;

    private readonly Channel<VisualFlattenRequest> _channel =
        Channel.CreateBounded<VisualFlattenRequest>(new BoundedChannelOptions(Capacity)
        {
            // Wait, paired with TryWrite (never WriteAsync), is what makes a full queue REPORT the
            // rejection. DropWrite looks safer but silently discards the item while TryWrite still
            // returns true — the de-dup marker below would then stay set forever for an item that
            // was never queued, so it could never be flattened again for the life of the process.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    private readonly ConcurrentDictionary<Guid, byte> _pending = new();
    private readonly ILogger<VisualFlattenQueue> _logger;

    public VisualFlattenQueue(ILogger<VisualFlattenQueue>? logger = null) =>
        _logger = logger ?? NullLogger<VisualFlattenQueue>.Instance;

    /// <inheritdoc />
    public bool Enqueue(VisualFlattenRequest request)
    {
        // Already queued for this item — the pass that eventually runs will read the latest markup
        // from the repository anyway, so a second entry would render the same state twice.
        if (!_pending.TryAdd(request.ItemId, 0))
        {
            return false;
        }

        if (_channel.Writer.TryWrite(request))
        {
            return true;
        }

        // Full or closed. Release the marker so a later save can try again, and let it go: a save
        // must never fail because its preview could not be scheduled.
        _pending.TryRemove(request.ItemId, out _);
        _logger.LogWarning(
            "VisualFlattenQueue: dropped flatten for item {ItemId} (queue full or closed). The preview stays stale until the next save.",
            request.ItemId);
        return false;
    }

    /// <summary>Consumed by the hosted worker. Clears the de-dup marker before yielding the item.</summary>
    public async IAsyncEnumerable<VisualFlattenRequest> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(ct))
        {
            // Cleared BEFORE the render so an edit that lands while this pass is running enqueues a
            // fresh one; otherwise that edit's preview would be silently skipped.
            _pending.TryRemove(request.ItemId, out _);
            yield return request;
        }
    }
}
