using System.Threading.Channels;
using SocialStudio.Application.Abstractions.ContentAnalysis;

namespace SocialStudio.Infrastructure.Social.Workflow;

/// <summary>
/// In-memory bounded queue for single-analysis jobs (Channel-backed). Registered as a singleton.
/// Bounded capacity applies backpressure under load; enqueue waits if the queue is full.
/// </summary>
public sealed class ChannelSingleAnalysisJobQueue : ISingleAnalysisJobQueue
{
    private readonly Channel<SingleAnalysisJob> _channel;

    public ChannelSingleAnalysisJobQueue()
    {
        _channel = Channel.CreateBounded<SingleAnalysisJob>(new BoundedChannelOptions(capacity: 200)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public ValueTask EnqueueAsync(SingleAnalysisJob job, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(job, cancellationToken);

    public IAsyncEnumerable<SingleAnalysisJob> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
