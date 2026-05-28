using System.Collections.Concurrent;
using Microsoft.Extensions.ObjectPool;

namespace CurlImpersonate.Http.Internal;

/// <summary>
/// Thread-safe pool of CurlEasyWrapper handles.
/// </summary>
internal sealed class CurlHandlePool : IDisposable
{
    private readonly ObjectPool<CurlEasyWrapper> _pool;
    private readonly ConcurrentDictionary<CurlEasyWrapper, byte> _allHandles = new();
    private bool _disposed;

    /// <summary>
    /// Creates a new handle pool with the specified options.
    /// </summary>
    public CurlHandlePool(CurlHandlerOptions options)
    {
        var policy = new CurlHandlePoolPolicy(_allHandles);
        _pool = new DefaultObjectPool<CurlEasyWrapper>(policy, options.MaxPoolSize);
    }

    /// <summary>
    /// Rents a handle from the pool.
    /// </summary>
    public CurlEasyWrapper Rent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _pool.Get();
    }

    /// <summary>
    /// Returns a handle to the pool after resetting it.
    /// </summary>
    public void Return(CurlEasyWrapper wrapper)
    {
        if (_disposed)
        {
            wrapper.Dispose();
            return;
        }

        wrapper.Reset();
        _pool.Return(wrapper);
    }

    /// <summary>
    /// Discards a handle without returning it to the pool.
    /// </summary>
    public void Discard(CurlEasyWrapper wrapper)
    {
        _allHandles.TryRemove(wrapper, out _);
        wrapper.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Dispose all tracked handles
        foreach (var handle in _allHandles.Keys)
        {
            handle.Dispose();
        }
        _allHandles.Clear();
    }
}

/// <summary>
/// Pool policy for creating and managing CurlEasyWrapper instances.
/// </summary>
internal sealed class CurlHandlePoolPolicy : PooledObjectPolicy<CurlEasyWrapper>
{
    private readonly ConcurrentDictionary<CurlEasyWrapper, byte> _tracker;

    public CurlHandlePoolPolicy(ConcurrentDictionary<CurlEasyWrapper, byte> tracker)
    {
        _tracker = tracker;
    }

    /// <inheritdoc />
    public override CurlEasyWrapper Create()
    {
        var wrapper = new CurlEasyWrapper();
        _tracker.TryAdd(wrapper, 0);
        return wrapper;
    }

    /// <inheritdoc />
    public override bool Return(CurlEasyWrapper wrapper)
    {
        // Only return healthy handles to the pool
        return wrapper.Handle != 0 && !wrapper.IsAborted;
    }
}
