using System.Threading.Channels;

namespace TwitchVault.Api.Recording;

public sealed class UploadQueue : IUploadQueue
{
    private readonly Channel<UploadBatch> _channel = Channel.CreateUnbounded<UploadBatch>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    public ValueTask QueueBatchAsync(UploadBatch batch, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(batch, cancellationToken);

    public IAsyncEnumerable<UploadBatch> ReadAllAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}