using System.Threading.Channels;
using Incoders.Hive.Application.Abstractions.Social.ContentGeneration;

namespace Incoders.Hive.Infrastructure.Social.ContentGeneration;

/// <summary>
/// In-memory bounded queue for content generation job ids (Channel-backed). Registered as a singleton.
/// Bounded capacity applies backpressure under load; enqueue waits if the queue is full.
/// Mirrors the pattern established by <c>ChannelSingleAnalysisJobQueue</c>.
/// </summary>
public sealed class ChannelContentGenerationJobQueue : IContentGenerationJobQueue
{
    private readonly Channel<Guid> _channel;

    public ChannelContentGenerationJobQueue(int capacity = 100)
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    /// <inheritdoc />
    public Task EnqueueAsync(Guid jobId, CancellationToken ct) =>
        _channel.Writer.WriteAsync(jobId, ct).AsTask();

    /// <inheritdoc />
    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
