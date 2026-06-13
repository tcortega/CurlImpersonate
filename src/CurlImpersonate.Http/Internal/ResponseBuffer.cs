using System.Buffers;

namespace CurlImpersonate.Http.Internal;

/// <summary>
/// ArrayPool-backed growable buffer for accumulating response body data.
/// </summary>
internal sealed class ResponseBuffer : IDisposable
{
    internal const int DefaultInitialCapacity = 4096;
    internal const int DefaultMaxRetainedCapacity = 128 * 1024;

    private byte[] _buffer;
    private int _position;
    private bool _disposed;
    private readonly int _initialCapacity;
    private readonly int _maxRetainedCapacity;
    private long? _maxLength;

    /// <summary>
    /// Creates a new response buffer with the specified initial capacity.
    /// </summary>
    public ResponseBuffer(
        int initialCapacity = DefaultInitialCapacity,
        int maxRetainedCapacity = DefaultMaxRetainedCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedCapacity, initialCapacity);

        _initialCapacity = initialCapacity;
        _maxRetainedCapacity = maxRetainedCapacity;
        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
    }

    /// <summary>
    /// Current length of written data.
    /// </summary>
    public int Length => _position;

    /// <summary>
    /// Maximum bytes that may be buffered.
    /// </summary>
    public long? MaxLength
    {
        get => _maxLength;
        set
        {
            if (value is <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum length must be positive.");

            _maxLength = value;
        }
    }

    /// <summary>
    /// Current rented buffer capacity.
    /// </summary>
    internal int Capacity => _buffer.Length;

    /// <summary>
    /// Writes data to the buffer, growing if necessary.
    /// </summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_position > Array.MaxLength - data.Length)
            throw new InvalidOperationException("Response body is too large to buffer.");

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
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_buffer.Length > _maxRetainedCapacity)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = ArrayPool<byte>.Shared.Rent(_initialCapacity);
        }

        _position = 0;
    }

    private void EnsureCapacity(int required)
    {
        if (_maxLength.HasValue && required > _maxLength.Value)
            throw new InvalidOperationException($"Response body exceeded the configured {nameof(MaxLength)} of {_maxLength.Value} bytes.");

        if (required <= _buffer.Length)
            return;

        if (required > Array.MaxLength)
            throw new InvalidOperationException("Response body is too large to buffer.");

        var doubledSize = _buffer.Length <= Array.MaxLength / 2
            ? _buffer.Length * 2
            : Array.MaxLength;
        var newSize = Math.Max(doubledSize, required);
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
