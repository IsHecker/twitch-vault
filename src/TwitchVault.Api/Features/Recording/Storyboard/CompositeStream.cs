using Stream = System.IO.Stream;

namespace TwitchVault.Api.Features.Recording.Storyboard;

/// <summary>
/// A read-only, seekable stream that concatenates two underlying streams sequentially in memory.
/// Used to prepend initialization headers (such as init.mp4) to media segments (fMP4) with zero disk I/O.
/// </summary>
public sealed class CompositeStream(Stream first, Stream second) : Stream
{
    private readonly long _firstLength = first.Length;
    private readonly long _totalLength = first.Length + second.Length;
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _totalLength;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _totalLength + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0 || target > _totalLength)
            throw new ArgumentOutOfRangeException(nameof(offset));

        _position = target;
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _totalLength || count <= 0)
            return 0;

        var totalRead = 0;

        if (_position < _firstLength)
        {
            first.Position = _position;
            var toRead = (int)Math.Min(count, _firstLength - _position);
            var read = first.Read(buffer, offset, toRead);
            totalRead += read;
            _position += read;
            offset += read;
            count -= read;
        }

        if (count > 0 && _position >= _firstLength && _position < _totalLength)
        {
            var secondPos = _position - _firstLength;
            second.Position = secondPos;
            var toRead = (int)Math.Min(count, _totalLength - _position);
            var read = second.Read(buffer, offset, toRead);
            totalRead += read;
            _position += read;
        }

        return totalRead;
    }

    public override int Read(Span<byte> buffer)
    {
        if (_position >= _totalLength || buffer.IsEmpty)
            return 0;

        var totalRead = 0;

        if (_position < _firstLength)
        {
            first.Position = _position;
            var toRead = (int)Math.Min(buffer.Length, _firstLength - _position);
            var read = first.Read(buffer[..toRead]);
            totalRead += read;
            _position += read;
            buffer = buffer[read..];
        }

        if (!buffer.IsEmpty && _position >= _firstLength && _position < _totalLength)
        {
            var secondPos = _position - _firstLength;
            second.Position = secondPos;
            var toRead = (int)Math.Min(buffer.Length, _totalLength - _position);
            var read = second.Read(buffer[..toRead]);
            totalRead += read;
            _position += read;
        }

        return totalRead;
    }

    public override void Flush() { }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // first.Dispose();
            second.Dispose();
        }

        base.Dispose(disposing);
    }
}