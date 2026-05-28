using CurlImpersonate.Http.Internal;

namespace CurlImpersonate.Http;

/// <summary>
/// HttpMessageHandler implementation using curl-impersonate for browser TLS fingerprint impersonation.
/// </summary>
public sealed class CurlHandler : HttpMessageHandler
{
    private static readonly Lazy<CurlEventLoop> SharedEventLoop = new(
        () => new CurlEventLoop(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly CurlEventLoop? _ownedEventLoop;
    private readonly CurlEventLoop _eventLoop;
    private readonly CurlHandlePool _pool;
    private readonly CurlHandlerOptions _options;
    private int _disposed;

    /// <summary>
    /// Creates a new CurlHandler with default options (Chrome142 impersonation).
    /// </summary>
    public CurlHandler() : this(new())
    {
    }

    /// <summary>
    /// Creates a new CurlHandler with the specified options.
    /// </summary>
    public CurlHandler(CurlHandlerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;

        if (options.UseSharedEventLoop)
        {
            _eventLoop = SharedEventLoop.Value;
        }
        else
        {
            _ownedEventLoop = new CurlEventLoop();
            _eventLoop = _ownedEventLoop;
        }

        _pool = new(options);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);
        cancellationToken.ThrowIfCancellationRequested();

        var wrapper = _pool.Rent();
        var success = false;
        try
        {
            // Configure the request
            await RequestMapper.ConfigureAsync(wrapper, request, _options, cancellationToken);

            // Execute the transfer
            var result = await _eventLoop.ExecuteAsync(wrapper, cancellationToken);

            // Build response
            var response = ResponseBuilder.Build(result, request);
            success = true;
            return response;
        }
        finally
        {
            if (success)
                _pool.Return(wrapper);
            else
                _pool.Discard(wrapper);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && disposing)
        {
            _ownedEventLoop?.Dispose();
            _pool.Dispose();
        }
        base.Dispose(disposing);
    }
}
