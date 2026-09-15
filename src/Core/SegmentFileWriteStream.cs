using pbl4.OSIntegration;

namespace pbl4.Core;

internal sealed class SegmentFileWriteStream : Stream
{
    private readonly ISafeFileWriter _writer;
    private readonly TokenBucketRateLimiter? _limiter;
    private readonly Action<int> _onBytesWritten;
    private readonly CancellationToken _cancellationToken;
    private long _offset;

    public SegmentFileWriteStream(
        ISafeFileWriter writer,
        long startOffset,
        TokenBucketRateLimiter? limiter,
        Action<int> onBytesWritten,
        CancellationToken cancellationToken)
    {
        _writer = writer;
        _offset = startOffset;
        _limiter = limiter;
        _onBytesWritten = onBytesWritten;
        _cancellationToken = cancellationToken;
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _offset;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
        => WriteAsync(buffer.AsMemory(offset, count), _cancellationToken).AsTask().GetAwaiter().GetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var linkedToken = cancellationToken.CanBeCanceled ? cancellationToken : _cancellationToken;
        if (_limiter is not null)
        {
            await _limiter.WaitAsync(buffer.Length, linkedToken);
        }

        await _writer.WriteAsync(_offset, buffer, linkedToken);
        _offset += buffer.Length;
        _onBytesWritten(buffer.Length);
    }
}

