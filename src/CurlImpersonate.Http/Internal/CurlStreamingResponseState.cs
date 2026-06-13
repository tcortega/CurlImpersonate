using System.Net;
using System.Threading.Channels;

namespace CurlImpersonate.Http.Internal;

internal sealed class CurlStreamingResponseState
{
    private const int BodyQueueCapacity = 16;

    private readonly CurlEasyWrapper _wrapper;
    private readonly CurlHandlePool _pool;
    private readonly long? _maxResponseBodyBytes;
    private readonly IDisposable? _cancellationLifetime;
    private readonly TaskCompletionSource<CurlResponseHeaders> _headersCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<byte[]> _body = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(BodyQueueCapacity)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly List<string> _headers = new();
    private long _bytesWritten;
    private int _statusCode;
    private Version? _version;
    private bool _currentHeaderBlockIsFinal;
    private Exception? _bodyException;
    private Action? _cancelTransfer;
    private Action? _resumeTransfer;
    private int _writerPaused;

    // Orders the response/completion handoff: SetResponse runs on the caller
    // thread and CompleteSuccess on the event loop thread, and exactly one of
    // them publishes the transfer metadata onto the response.
    private readonly object _metadataGate = new();

    // Weak so an abandoned response (and its body stream) stays collectable:
    // this state is rooted via the pooled wrapper, and a strong reference
    // here would keep the stream alive and defeat its finalizer backstop.
    private WeakReference<HttpResponseMessage>? _response;
    private CurlResponse? _completedResponse;
    private bool _returnToPoolAfterTransfer;
    private int _transferCompleted;
    private int _bodyStreamClosed;
    private int _released;

    public CurlStreamingResponseState(
        CurlEasyWrapper wrapper,
        CurlHandlePool pool,
        long? maxResponseBodyBytes,
        IDisposable? cancellationLifetime = null)
    {
        _wrapper = wrapper;
        _pool = pool;
        _maxResponseBodyBytes = maxResponseBodyBytes;
        _cancellationLifetime = cancellationLifetime;
    }

    public Task<CurlResponseHeaders> HeadersTask => _headersCompletion.Task;

    public void SetCancelTransfer(Action cancelTransfer)
    {
        _cancelTransfer = cancelTransfer;
    }

    public void SetResumeTransfer(Action resumeTransfer)
    {
        _resumeTransfer = resumeTransfer;
    }

    public void SetResponse(HttpResponseMessage response)
    {
        lock (_metadataGate)
        {
            _response = new WeakReference<HttpResponseMessage>(response);
            if (_completedResponse is { } completedResponse)
                SetResponseMetadata(response, completedResponse);
        }
    }

    public Stream OpenBodyStream() => new CurlStreamingResponseStream(this);

    public void AddHeaderLine(string headerLine)
    {
        if (headerLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
        {
            _headers.Clear();
            _currentHeaderBlockIsFinal = TryParseStatusLine(headerLine, out _statusCode, out _version) &&
                                         !IsInformationalStatus(_statusCode);
            return;
        }

        _headers.Add(headerLine);
    }

    public void CompleteHeaderBlock()
    {
        if (!_currentHeaderBlockIsFinal)
            return;

        _headersCompletion.TrySetResult(new CurlResponseHeaders(
            _statusCode,
            _headers.ToArray(),
            _version));
    }

    /// <summary>
    /// Queues a body chunk for the consumer. Returns false when the queue is
    /// full; the caller must pause the transfer and the chunk is not consumed.
    /// </summary>
    public bool WriteBody(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return true;

        if (_bytesWritten > long.MaxValue - data.Length)
            throw new InvalidOperationException("Streaming response body byte count is too large.");

        var nextTotal = _bytesWritten + data.Length;
        if (_maxResponseBodyBytes is { } maxBytes && nextTotal > maxBytes)
        {
            throw new InvalidOperationException(
                $"Response body exceeded the configured {nameof(CurlHandlerOptions.MaxResponseBodyBytes)} of {maxBytes} bytes.");
        }

        var chunk = data.ToArray();
        if (!_body.Writer.TryWrite(chunk))
        {
            // Publish the paused flag before retrying: if the reader drains
            // the queue in between, either the retry succeeds or the reader
            // sees the flag and resumes the transfer after its next read.
            Volatile.Write(ref _writerPaused, 1);
            if (!_body.Writer.TryWrite(chunk))
                return false;

            Volatile.Write(ref _writerPaused, 0);
        }

        _bytesWritten = nextTotal;
        return true;
    }

    public async ValueTask<byte[]?> ReadChunkAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                if (!await _body.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (_bodyException is not null)
                        throw CreateBodyReadException(_bodyException);

                    return null;
                }
            }
            // The channel propagates its completion error from WaitToReadAsync
            // either directly or wrapped in ChannelClosedException.
            catch (Exception ex) when (_bodyException is { } bodyException &&
                                       (ReferenceEquals(ex, bodyException) || ex is ChannelClosedException))
            {
                throw CreateBodyReadException(bodyException);
            }

            if (_body.Reader.TryRead(out var chunk))
            {
                if (Interlocked.Exchange(ref _writerPaused, 0) == 1)
                    _resumeTransfer?.Invoke();

                return chunk;
            }
        }
    }

    // Body reads are stream reads, so transport failures must surface as the
    // IOException family that HttpContent and SocketsHttpHandler consumers
    // handle; other exceptions pass through unchanged.
    private static Exception CreateBodyReadException(Exception exception)
    {
        return exception is CurlException or CurlMultiException
            ? CurlNativeErrors.WrapBodyReadException(exception)
            : exception;
    }

    public void CancelTransfer()
    {
        CompleteBodyStream(cancelTransfer: true);
    }

    public void CompleteBodyStream(bool cancelTransfer)
    {
        if (Interlocked.Exchange(ref _bodyStreamClosed, 1) == 1)
            return;

        if (cancelTransfer && Volatile.Read(ref _transferCompleted) == 0)
        {
            _body.Writer.TryComplete();
            _cancelTransfer?.Invoke();
        }

        TryReleaseCompletedTransfer();
    }

    public void CompleteSuccess(CurlResponse response)
    {
        CompleteHeadersFromResponse(response);

        lock (_metadataGate)
        {
            _completedResponse = response;
            if (_response?.TryGetTarget(out var responseMessage) == true)
                SetResponseMetadata(responseMessage, response);
        }

        // Publish the pool decision before the release store so a consumer that
        // observes _transferCompleted also observes _returnToPoolAfterTransfer.
        _returnToPoolAfterTransfer = true;
        Volatile.Write(ref _transferCompleted, 1);

        _body.Writer.TryComplete();
        TryReleaseCompletedTransfer();
    }

    public void CompleteException(Exception exception)
    {
        Volatile.Write(ref _transferCompleted, 1);
        _bodyException = exception;
        _headersCompletion.TrySetException(exception);
        _body.Writer.TryComplete(exception);
        TryReleaseCompletedTransfer();
    }

    public void CompleteCanceled(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _transferCompleted, 1);
        if (cancellationToken.IsCancellationRequested)
            _headersCompletion.TrySetCanceled(cancellationToken);
        else
            _headersCompletion.TrySetCanceled();

        _body.Writer.TryComplete(new OperationCanceledException(cancellationToken));
        TryReleaseCompletedTransfer();
    }

    public void DiscardBeforeStart()
    {
        _body.Writer.TryComplete();
        Release(returnToPool: false);
    }

    private void CompleteHeadersFromResponse(CurlResponse response)
    {
        _headersCompletion.TrySetResult(new CurlResponseHeaders(
            response.StatusCode,
            response.Headers,
            response.Version));
    }

    private void Release(bool returnToPool)
    {
        if (Interlocked.Exchange(ref _released, 1) == 1)
            return;

        if (returnToPool)
            _pool.Return(_wrapper);
        else
            _pool.Discard(_wrapper);

        _cancellationLifetime?.Dispose();
    }

    private void TryReleaseCompletedTransfer()
    {
        if (Volatile.Read(ref _transferCompleted) == 0)
            return;

        if (_headersCompletion.Task.IsCompletedSuccessfully &&
            Volatile.Read(ref _bodyStreamClosed) == 0)
        {
            return;
        }

        Release(_returnToPoolAfterTransfer);
    }

    private static bool TryParseStatusLine(string line, out int statusCode, out Version? version)
    {
        statusCode = 0;
        version = null;

        var firstSpace = line.IndexOf(' ');
        if (firstSpace <= 0 || firstSpace + 4 > line.Length)
            return false;

        version = ParseHttpVersion(line[..firstSpace]);
        return int.TryParse(line.AsSpan(firstSpace + 1, 3), out statusCode);
    }

    private static Version ParseHttpVersion(string value)
    {
        return value switch
        {
            "HTTP/1.0" => HttpVersion.Version10,
            "HTTP/2" or "HTTP/2.0" => HttpVersion.Version20,
            "HTTP/3" or "HTTP/3.0" => HttpVersion.Version30,
            _ => HttpVersion.Version11
        };
    }

    private static bool IsInformationalStatus(int statusCode)
    {
        return statusCode >= 100 && statusCode < 200 && statusCode != 101;
    }

    private static void SetResponseMetadata(HttpResponseMessage response, CurlResponse curlResponse)
    {
        CurlResponseMetadata.Set(
            response,
            curlResponse.EffectiveUri,
            curlResponse.RedirectCount,
            curlResponse.Metrics);
    }
}
