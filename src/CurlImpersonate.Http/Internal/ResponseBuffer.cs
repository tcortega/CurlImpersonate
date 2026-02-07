using System.Buffers;

namespace CurlImpersonate.Http.Internal;

/// <summary>
/// ArrayPool-backed growable buffer for accumulating response body data.
/// </summary>
internal sealed class ResponseBuffer : IDisposable
{
    private byte[] _buffer;
    private int _position;
    private bool _disposed;

    /// <summary>
    /// Creates a new response buffer with the specified initial capacity.
    /// </summary>
    public ResponseBuffer(int initialCapacity = 4096)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    /// <summary>
    /// Current length of written data.
    /// </summary>
    public int Length => _position;

    /// <summary>
    /// Writes data to the buffer, growing if necessary.
    /// </summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureCapacity(_position + data.Length);
        data.CopyTo(_buffer.AsSpan(_position));
        _position += data.Length;
    }

    /// <summary>
    /// Gets the written content as a ReadOnlyMemory.
    /// </summary>
    public ReadOnlyMemory<byte> GetContent() => _buffer.AsMemory(0, _position);

    /// <summary>
    /// Copies the written content to a new byte array.
    /// </summary>
    public byte[] ToArray() => _buffer.AsSpan(0, _position).ToArray();

    /// <summary>
    /// Resets the buffer for reuse without releasing the underlying array.
    /// </summary>
    public void Reset()
    {
        _position = 0;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _buffer.Length)
            return;

        var newSize = Math.Max(_buffer.Length * 2, required);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        _buffer.AsSpan(0, _position).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = null!;
    }
}
