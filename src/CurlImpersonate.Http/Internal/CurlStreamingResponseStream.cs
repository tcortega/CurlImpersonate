namespace CurlImpersonate.Http.Internal;

internal sealed class CurlStreamingResponseStream : Stream
{
    private readonly CurlStreamingResponseState _state;
    private byte[]? _currentChunk;
    private int _currentOffset;
    private bool _completed;
    private bool _disposed;

    public CurlStreamingResponseStream(CurlStreamingResponseState state)
    {
        _state = state;
    }

    // Backstop for responses abandoned without disposal: without it the
    // transfer would stay pending and the pooled native handle would leak
    // for the process lifetime.
    ~CurlStreamingResponseStream()
    {
        Dispose(false);
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateRead(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.Length == 0)
            return 0;

        while (true)
        {
            if (TryCopyCurrentChunk(buffer, out var copied))
                return copied;

            _currentChunk = _state.ReadChunkAsync(CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            _currentOffset = 0;
            if (_currentChunk is null)
            {
                _completed = true;
                _state.CompleteBodyStream(cancelTransfer: false);
                return 0;
            }
        }
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.Length == 0)
            return 0;

        while (true)
        {
            if (TryCopyCurrentChunk(buffer.Span, out var copied))
                return copied;

            _currentChunk = await _state.ReadChunkAsync(cancellationToken).ConfigureAwait(false);
            _currentOffset = 0;
            if (_currentChunk is null)
            {
                _completed = true;
                _state.CompleteBodyStream(cancelTransfer: false);
                return 0;
            }
        }
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateRead(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (!_completed)
            {
                try
                {
                    _state.CancelTransfer();
                }
                catch when (!disposing)
                {
                    // Never throw from the finalizer thread.
                }
            }
        }

        base.Dispose(disposing);
    }

    private static void ValidateRead(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count)
            throw new ArgumentException("The offset and count exceed the buffer length.");
    }

    private bool TryCopyCurrentChunk(Span<byte> buffer, out int copied)
    {
        copied = 0;
        if (_currentChunk is null)
            return false;

        var available = _currentChunk.Length - _currentOffset;
        if (available <= 0)
        {
            _currentChunk = null;
            _currentOffset = 0;
            return false;
        }

        copied = Math.Min(buffer.Length, available);
        _currentChunk.AsSpan(_currentOffset, copied).CopyTo(buffer);
        _currentOffset += copied;

        if (_currentOffset == _currentChunk.Length)
        {
            _currentChunk = null;
            _currentOffset = 0;
        }

        return true;
    }
}
