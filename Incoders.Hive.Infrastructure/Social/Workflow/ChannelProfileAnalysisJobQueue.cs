using System.Threading.Channels;
using Incoders.Hive.Application.Abstractions.Social;

namespace Incoders.Hive.Infrastructure.Social.Workflow;

/// <summary>
/// In-memory bounded queue for profile-analysis jobs (Channel-backed). Registered as a singleton.
/// Bounded capacity applies backpressure under load; enqueue waits if the queue is full.
/// Mirrors <see cref="ChannelSingleAnalysisJobQueue"/> for the profile-analysis pipeline.
/// </summary>
public sealed class ChannelProfileAnalysisJobQueue : IProfileAnalysisJobQueue
{
    private readonly Channel<ProfileAnalysisJob> _channel;

    public ChannelProfileAnalysisJobQueue()
    {
        _channel = Channel.CreateBounded<ProfileAnalysisJob>(new BoundedChannelOptions(capacity: 200)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public ValueTask EnqueueAsync(ProfileAnalysisJob job, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(job, cancellationToken);

    public IAsyncEnumerable<ProfileAnalysisJob> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
