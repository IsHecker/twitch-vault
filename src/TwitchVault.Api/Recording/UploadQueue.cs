using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace TwitchVault.Api.Recording;

/// <summary>
/// A two-tier FIFO queue: urgent batches are always handed out before regular ones,
/// as long as any urgent batch is waiting, regardless of arrival order.
/// </summary>
public sealed class UploadQueue : IUploadQueue
{
    private readonly Channel<UploadBatch> _urgentChannel = Channel.CreateUnbounded<UploadBatch>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    private readonly Channel<UploadBatch> _regularChannel = Channel.CreateUnbounded<UploadBatch>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    public ValueTask QueueBatchAsync(UploadBatch batch, CancellationToken cancellationToken = default)
    {
        var targetChannel = batch.IsUrgent ? _urgentChannel : _regularChannel;
        return targetChannel.Writer.WriteAsync(batch, cancellationToken);
    }

    public async IAsyncEnumerable<UploadBatch> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var urgentOpen = true;
        var regularOpen = true;

        while (!cancellationToken.IsCancellationRequested && (urgentOpen || regularOpen))
        {
            if (urgentOpen && _urgentChannel.Reader.TryRead(out var urgentBatch))
            {
                yield return urgentBatch;
                continue;
            }

            if (regularOpen && _regularChannel.Reader.TryRead(out var regularBatch))
            {
                yield return regularBatch;
                continue;
            }

            var pendingWaits = new List<Task<bool>>(2);
            Task<bool>? urgentReady = null;
            Task<bool>? regularReady = null;

            if (urgentOpen)
            {
                urgentReady = _urgentChannel.Reader.WaitToReadAsync(cancellationToken).AsTask();
                pendingWaits.Add(urgentReady);
            }

            if (regularOpen)
            {
                regularReady = _regularChannel.Reader.WaitToReadAsync(cancellationToken).AsTask();
                pendingWaits.Add(regularReady);
            }

            await Task.WhenAny(pendingWaits);

            // A completed WaitToReadAsync result of false means that channel's writer is done
            // and fully drained - stop creating new waits for it.
            if (urgentReady is { IsCompletedSuccessfully: true, Result: false })
                urgentOpen = false;

            if (regularReady is { IsCompletedSuccessfully: true, Result: false })
                regularOpen = false;
        }
    }
}