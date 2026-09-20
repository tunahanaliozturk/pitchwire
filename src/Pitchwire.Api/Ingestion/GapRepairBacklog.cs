using System.Threading.Channels;

namespace Pitchwire.Api.Ingestion;

/// <summary>
/// Repairs waiting to be attempted, handed from the request that noticed the hole to the worker that
/// closes it.
/// </summary>
/// <remarks>
/// The channel is bounded and full writes wait rather than drop. With DropWrite, TryWrite returns
/// true and throws the item away, so a caller checking its return value never learns that the repair
/// it queued has vanished, and the match stays degraded with nothing on its way to fix it.
/// </remarks>
public sealed class GapRepairBacklog
{
    private readonly Channel<GapRepairRequest> _channel = Channel.CreateBounded<GapRepairRequest>(
        new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public bool TryEnqueue(GapRepairRequest request) => _channel.Writer.TryWrite(request);

    public IAsyncEnumerable<GapRepairRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
