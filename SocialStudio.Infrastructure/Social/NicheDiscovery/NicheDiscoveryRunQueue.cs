using System.Threading.Channels;
using SocialStudio.Application.Abstractions.Social.NicheDiscovery;
using SocialStudio.Domain.Social.NicheDiscovery;

namespace SocialStudio.Infrastructure.Social.NicheDiscovery;

/// <summary>
/// In-memory bounded queue for discovery-run jobs (Channel-backed). Registered as a singleton.
/// Mirrors <c>ChannelProfileAnalysisJobQueue</c> — bounded capacity applies backpressure under load;
/// enqueue waits if the queue is full.
/// </summary>
public sealed class NicheDiscoveryRunQueue : INicheDiscoveryRunQueue
{
    private readonly Channel<(Guid OrganizationId, DiscoveryRunId RunId)> _channel;

    public NicheDiscoveryRunQueue()
    {
        _channel = Channel.CreateBounded<(Guid, DiscoveryRunId)>(new BoundedChannelOptions(capacity: 200)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public Task EnqueueAsync(Guid organizationId, DiscoveryRunId runId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync((organizationId, runId), ct).AsTask();

    public async Task<(Guid OrganizationId, DiscoveryRunId RunId)?> DequeueAsync(CancellationToken ct = default)
    {
        try
        {
            return await _channel.Reader.ReadAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }
}
