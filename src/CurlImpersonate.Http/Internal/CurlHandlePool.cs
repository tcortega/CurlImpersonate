using System.Collections.Concurrent;

namespace CurlImpersonate.Http.Internal;

/// <summary>
/// Thread-safe pool of CurlEasyWrapper handles.
/// </summary>
internal sealed class CurlHandlePool : IDisposable
{
    private readonly Stack<CurlEasyWrapper> _idle;
    private readonly ConcurrentDictionary<CurlEasyWrapper, byte> _allHandles = new();
    private readonly ConcurrentDictionary<CurlEasyWrapper, byte> _activeHandles = new();
    private readonly object _stateLock = new();
    private readonly int _maxPoolSize;
    private bool _disposed;

    /// <summary>
    /// Creates a new handle pool with the specified options.
    /// </summary>
    public CurlHandlePool(CurlHandlerOptions options)
    {
        _maxPoolSize = options.MaxPoolSize;
        _idle = new Stack<CurlEasyWrapper>(_maxPoolSize);
    }

    /// <summary>
    /// Rents a handle from the pool.
    /// </summary>
    public CurlEasyWrapper Rent()
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_idle.TryPop(out var wrapper))
            {
                wrapper = new CurlEasyWrapper();
                _allHandles.TryAdd(wrapper, 0);
            }

            _activeHandles.TryAdd(wrapper, 0);
            return wrapper;
        }
    }

    /// <summary>
    /// Returns a handle to the pool after resetting it, or disposes it when
    /// the pool is full, disposed, or the handle cannot be reused.
    /// </summary>
    public void Return(CurlEasyWrapper wrapper)
    {
        if (_disposed)
        {
            Discard(wrapper);
            return;
        }

        if (!wrapper.CanReuse)
        {
            Discard(wrapper);
            return;
        }

        try
        {
            wrapper.Reset();

            lock (_stateLock)
            {
                if (!_disposed && wrapper.CanReuse && _idle.Count < _maxPoolSize)
                {
                    _activeHandles.TryRemove(wrapper, out _);
                    _idle.Push(wrapper);
                    return;
                }
            }

            Discard(wrapper);
        }
        catch
        {
            Discard(wrapper);
        }
    }

    /// <summary>
    /// Discards a handle without returning it to the pool.
    /// </summary>
    public void Discard(CurlEasyWrapper wrapper)
    {
        lock (_stateLock)
        {
            _activeHandles.TryRemove(wrapper, out _);
            _allHandles.TryRemove(wrapper, out _);
        }

        wrapper.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CurlEasyWrapper[] idleHandles;

        lock (_stateLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _idle.Clear();
            idleHandles = _allHandles.Keys
                .Where(handle => !_activeHandles.ContainsKey(handle))
                .ToArray();

            foreach (var handle in idleHandles)
            {
                _allHandles.TryRemove(handle, out _);
            }
        }

        foreach (var handle in idleHandles)
        {
            handle.Dispose();
        }
    }
}
