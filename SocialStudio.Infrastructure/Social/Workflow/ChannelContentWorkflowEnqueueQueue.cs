using System.Threading.Channels;
using SocialStudio.Application.Abstractions.Social;

namespace SocialStudio.Infrastructure.Social.Workflow;

/// <summary>
/// In-memory bounded queue for workflow candidate enqueue items (Channel-backed), mirroring
/// <see cref="ChannelSingleAnalysisJobQueue"/>. Registered as a singleton: one channel shared by the
/// enqueue command handler (producer) and <c>ContentWorkflowEnqueueBackgroundWorker</c> (consumer).
/// </summary>
public sealed class ChannelContentWorkflowEnqueueQueue : IContentWorkflowEnqueueQueue
{
    private readonly Channel<ContentWorkflowEnqueueItem> _channel;

    public ChannelContentWorkflowEnqueueQueue()
    {
        _channel = Channel.CreateBounded<ContentWorkflowEnqueueItem>(new BoundedChannelOptions(capacity: 200)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public ValueTask EnqueueAsync(ContentWorkflowEnqueueItem item, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<ContentWorkflowEnqueueItem> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
