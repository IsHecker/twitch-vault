using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace TwitchVault.Api.Recording;

public sealed class UploadQueue : IUploadQueue
{
    private readonly Channel<UploadBatch> _urgentChannel = Channel.CreateUnbounded<UploadBatch>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    private readonly Channel<UploadBatch> _regularChannel = Channel.CreateUnbounded<UploadBatch>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    private readonly SemaphoreSlim _dataAvailable = new(0, 1);
    private int _signal;

    public ValueTask QueueBatchAsync(UploadBatch batch, CancellationToken cancellationToken = default)
    {
        var target = batch.IsUrgent ? _urgentChannel : _regularChannel;
        var write = target.Writer.WriteAsync(batch, cancellationToken);

        if (write.IsCompletedSuccessfully)
        {
            Signal();
            return ValueTask.CompletedTask;
        }
        return AwaitWrite(write, this);

        static async ValueTask AwaitWrite(ValueTask writeTask, UploadQueue self)
        {
            await writeTask.ConfigureAwait(false);
            self.Signal();
        }
    }

    public async IAsyncEnumerable<UploadBatch> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_urgentChannel.Reader.TryRead(out var urgentBatch))
            {
                yield return urgentBatch;
                continue;
            }

            if (_regularChannel.Reader.TryRead(out var regularBatch))
            {
                yield return regularBatch;
                continue;
            }

            if (_urgentChannel.Reader.Completion.IsCompleted &&
                _regularChannel.Reader.Completion.IsCompleted)
                yield break;

            await _dataAvailable.WaitAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _signal, 0);
        }
    }

    public void Complete()
    {
        _urgentChannel.Writer.Complete();
        _regularChannel.Writer.Complete();
        Signal();
    }

    private void Signal()
    {
        if (Interlocked.Exchange(ref _signal, 1) == 0)
            _dataAvailable.Release();
    }
}